#include "ProcessInspection.h"
#include "UniqueHandle.h"

#include <WinSock2.h>
#include <WS2tcpip.h>
#include <Windows.h>
#include <Iphlpapi.h>
#include <TlHelp32.h>

#include <algorithm>
#include <array>
#include <vector>

namespace
{
    using traceforge::agent::ConnectionDetails;
    using traceforge::agent::ProcessInspection;
    using traceforge::agent::UniqueHandle;

    [[nodiscard]] std::string FormatAddress(int family, const void* address)
    {
        std::array<char, INET6_ADDRSTRLEN> buffer{};
        return InetNtopA(family, const_cast<void*>(address), buffer.data(), buffer.size())
            ? std::string(buffer.data())
            : "Unavailable";
    }

    [[nodiscard]] std::string TcpState(DWORD state)
    {
        switch (state)
        {
        case MIB_TCP_STATE_CLOSED: return "Closed";
        case MIB_TCP_STATE_LISTEN: return "Listening";
        case MIB_TCP_STATE_SYN_SENT: return "SYN sent";
        case MIB_TCP_STATE_SYN_RCVD: return "SYN received";
        case MIB_TCP_STATE_ESTAB: return "Established";
        case MIB_TCP_STATE_FIN_WAIT1: return "FIN wait 1";
        case MIB_TCP_STATE_FIN_WAIT2: return "FIN wait 2";
        case MIB_TCP_STATE_CLOSE_WAIT: return "Close wait";
        case MIB_TCP_STATE_CLOSING: return "Closing";
        case MIB_TCP_STATE_LAST_ACK: return "Last ACK";
        case MIB_TCP_STATE_TIME_WAIT: return "Time wait";
        case MIB_TCP_STATE_DELETE_TCB: return "Deleting";
        default: return "Unknown";
        }
    }

    template<typename Table, typename Loader>
    [[nodiscard]] DWORD LoadTable(Loader&& loader, std::vector<std::uint64_t>& buffer, const Table*& table)
    {
        DWORD size = 0;
        DWORD error = loader(nullptr, &size);
        if (error != ERROR_INSUFFICIENT_BUFFER)
        {
            return error;
        }

        for (int attempt = 0; attempt < 4; ++attempt)
        {
            buffer.resize((static_cast<std::size_t>(size) + sizeof(std::uint64_t) - 1) / sizeof(std::uint64_t));
            error = loader(buffer.data(), &size);
            if (error == NO_ERROR)
            {
                table = reinterpret_cast<const Table*>(buffer.data());
                return NO_ERROR;
            }
            if (error != ERROR_INSUFFICIENT_BUFFER)
            {
                return error;
            }
        }

        return ERROR_INSUFFICIENT_BUFFER;
    }

    void CollectThreads(ProcessInspection& result)
    {
        const UniqueHandle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0));
        if (!snapshot.IsValid())
        {
            result.threadError = GetLastError();
            return;
        }

        THREADENTRY32 entry{};
        entry.dwSize = sizeof(entry);
        if (!Thread32First(snapshot.Get(), &entry))
        {
            const DWORD error = GetLastError();
            result.threadError = error == ERROR_NO_MORE_FILES ? ERROR_SUCCESS : error;
            return;
        }

        do
        {
            if (entry.th32OwnerProcessID == result.processId)
            {
                result.threads.push_back({entry.th32ThreadID, entry.tpBasePri});
            }
        }
        while (Thread32Next(snapshot.Get(), &entry));

        const DWORD error = GetLastError();
        result.threadError = error == ERROR_NO_MORE_FILES ? ERROR_SUCCESS : error;
    }

    void CollectIdentity(ProcessInspection& result)
    {
        const UniqueHandle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, result.processId));
        if (!process.IsValid())
        {
            result.identityError = GetLastError();
            return;
        }

        FILETIME creation{}, exit{}, kernel{}, user{};
        if (!GetProcessTimes(process.Get(), &creation, &exit, &kernel, &user))
        {
            result.identityError = GetLastError();
            return;
        }

        ULARGE_INTEGER value{};
        value.LowPart = creation.dwLowDateTime;
        value.HighPart = creation.dwHighDateTime;
        constexpr std::uint64_t WindowsToUnixMilliseconds = 11644473600000ULL;
        result.startTimeUnixMs = static_cast<std::int64_t>(
            value.QuadPart / 10000ULL - WindowsToUnixMilliseconds);
    }

    void CollectModules(ProcessInspection& result)
    {
        UniqueHandle snapshot;
        for (int attempt = 0; attempt < 4; ++attempt)
        {
            snapshot.Reset(CreateToolhelp32Snapshot(
                TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32,
                result.processId));
            if (snapshot.IsValid() || GetLastError() != ERROR_BAD_LENGTH)
            {
                break;
            }
        }

        if (!snapshot.IsValid())
        {
            result.moduleError = GetLastError();
            return;
        }

        MODULEENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        if (!Module32FirstW(snapshot.Get(), &entry))
        {
            const DWORD error = GetLastError();
            result.moduleError = error == ERROR_NO_MORE_FILES ? ERROR_SUCCESS : error;
            return;
        }

        do
        {
            result.modules.push_back({entry.szModule, entry.szExePath, entry.modBaseSize});
        }
        while (Module32NextW(snapshot.Get(), &entry));

        const DWORD error = GetLastError();
        result.moduleError = error == ERROR_NO_MORE_FILES ? ERROR_SUCCESS : error;
    }

    void CollectTcp(ProcessInspection& result, ULONG family)
    {
        std::vector<std::uint64_t> buffer;
        const auto loader = [family](void* data, DWORD* size)
        {
            return GetExtendedTcpTable(data, size, FALSE, family, TCP_TABLE_OWNER_PID_ALL, 0);
        };

        if (family == AF_INET)
        {
            const MIB_TCPTABLE_OWNER_PID* table = nullptr;
            const DWORD error = LoadTable(loader, buffer, table);
            if (error != NO_ERROR)
            {
                result.networkError = error;
                return;
            }

            for (DWORD index = 0; index < table->dwNumEntries; ++index)
            {
                const auto& row = table->table[index];
                if (row.dwOwningPid == result.processId)
                {
                    result.connections.push_back({
                        "TCP4",
                        FormatAddress(AF_INET, &row.dwLocalAddr),
                        ntohs(static_cast<u_short>(row.dwLocalPort)),
                        FormatAddress(AF_INET, &row.dwRemoteAddr),
                        ntohs(static_cast<u_short>(row.dwRemotePort)),
                        TcpState(row.dwState)});
                }
            }
            return;
        }

        const MIB_TCP6TABLE_OWNER_PID* table = nullptr;
        const DWORD error = LoadTable(loader, buffer, table);
        if (error != NO_ERROR)
        {
            result.networkError = error;
            return;
        }

        for (DWORD index = 0; index < table->dwNumEntries; ++index)
        {
            const auto& row = table->table[index];
            if (row.dwOwningPid == result.processId)
            {
                result.connections.push_back({
                    "TCP6",
                    FormatAddress(AF_INET6, row.ucLocalAddr),
                    ntohs(static_cast<u_short>(row.dwLocalPort)),
                    FormatAddress(AF_INET6, row.ucRemoteAddr),
                    ntohs(static_cast<u_short>(row.dwRemotePort)),
                    TcpState(row.dwState)});
            }
        }
    }

    void CollectUdp(ProcessInspection& result, ULONG family)
    {
        std::vector<std::uint64_t> buffer;
        const auto loader = [family](void* data, DWORD* size)
        {
            return GetExtendedUdpTable(data, size, FALSE, family, UDP_TABLE_OWNER_PID, 0);
        };

        if (family == AF_INET)
        {
            const MIB_UDPTABLE_OWNER_PID* table = nullptr;
            const DWORD error = LoadTable(loader, buffer, table);
            if (error != NO_ERROR)
            {
                result.networkError = error;
                return;
            }

            for (DWORD index = 0; index < table->dwNumEntries; ++index)
            {
                const auto& row = table->table[index];
                if (row.dwOwningPid == result.processId)
                {
                    result.connections.push_back({
                        "UDP4", FormatAddress(AF_INET, &row.dwLocalAddr),
                        ntohs(static_cast<u_short>(row.dwLocalPort)), "", 0, "Bound"});
                }
            }
            return;
        }

        const MIB_UDP6TABLE_OWNER_PID* table = nullptr;
        const DWORD error = LoadTable(loader, buffer, table);
        if (error != NO_ERROR)
        {
            result.networkError = error;
            return;
        }

        for (DWORD index = 0; index < table->dwNumEntries; ++index)
        {
            const auto& row = table->table[index];
            if (row.dwOwningPid == result.processId)
            {
                result.connections.push_back({
                    "UDP6", FormatAddress(AF_INET6, row.ucLocalAddr),
                    ntohs(static_cast<u_short>(row.dwLocalPort)), "", 0, "Bound"});
            }
        }
    }
}

namespace traceforge::agent
{
    ProcessInspection ProcessInspector::Inspect(std::uint32_t processId)
    {
        ProcessInspection result{};
        result.processId = processId;
        CollectIdentity(result);
        CollectThreads(result);
        CollectModules(result);
        CollectTcp(result, AF_INET);
        CollectTcp(result, AF_INET6);
        CollectUdp(result, AF_INET);
        CollectUdp(result, AF_INET6);
        return result;
    }
}
