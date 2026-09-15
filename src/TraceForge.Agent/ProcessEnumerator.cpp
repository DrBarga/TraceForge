#include "ProcessEnumerator.h"
#include "UniqueHandle.h"

#include <Psapi.h>
#include <TlHelp32.h>

#include <algorithm>
#include <system_error>
#include <unordered_set>

namespace
{
    [[noreturn]] void ThrowWin32Error(DWORD error, const char* operation)
    {
        throw std::system_error(
            static_cast<int>(error),
            std::system_category(),
            operation);
    }

    [[nodiscard]] std::uint64_t FileTimeToUInt64(const FILETIME& value) noexcept
    {
        ULARGE_INTEGER result{};
        result.LowPart = value.dwLowDateTime;
        result.HighPart = value.dwHighDateTime;
        return result.QuadPart;
    }

    [[nodiscard]] std::wstring QueryExecutablePath(HANDLE process)
    {
        std::wstring path(32768, L'\0');
        DWORD length = static_cast<DWORD>(path.size());

        if (!QueryFullProcessImageNameW(process, 0, path.data(), &length))
        {
            return {};
        }

        path.resize(length);
        return path;
    }

    [[nodiscard]] std::uint64_t QueryWorkingSet(HANDLE process) noexcept
    {
        PROCESS_MEMORY_COUNTERS_EX counters{};
        counters.cb = sizeof(counters);

        if (!K32GetProcessMemoryInfo(
                process,
                reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters),
                sizeof(counters)))
        {
            return 0;
        }

        return static_cast<std::uint64_t>(counters.WorkingSetSize);
    }
}

namespace traceforge::agent
{
    std::vector<ProcessInfo> ProcessEnumerator::Enumerate()
    {
        const UniqueHandle snapshot(
            CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));

        if (!snapshot.IsValid())
        {
            ThrowWin32Error(GetLastError(), "CreateToolhelp32Snapshot");
        }

        PROCESSENTRY32W entry{};
        entry.dwSize = sizeof(entry);

        if (!Process32FirstW(snapshot.Get(), &entry))
        {
            ThrowWin32Error(GetLastError(), "Process32FirstW");
        }

        std::vector<ProcessInfo> processes;
        std::unordered_set<std::uint32_t> seenProcessIds;
        const std::uint64_t wallTimeMs = GetTickCount64();
        const DWORD processorCount = std::max<DWORD>(1, GetActiveProcessorCount(ALL_PROCESSOR_GROUPS));

        do
        {
            const auto processId = static_cast<std::uint32_t>(entry.th32ProcessID);
            seenProcessIds.insert(processId);

            ProcessInfo info{};
            info.processId = processId;
            info.name = entry.szExeFile;
            info.threadCount = static_cast<std::uint32_t>(entry.cntThreads);

            DWORD accessError = ERROR_SUCCESS;
            UniqueHandle process(
                OpenProcess(
                    PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ,
                    FALSE,
                    processId));

            if (!process.IsValid())
            {
                accessError = GetLastError();
                process.Reset(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId));
                if (!process.IsValid())
                {
                    accessError = GetLastError();
                }
            }

            info.accessible = process.IsValid();
            info.accessError = info.accessible ? ERROR_SUCCESS : accessError;

            if (process.IsValid())
            {
                info.executablePath = QueryExecutablePath(process.Get());
                info.workingSetBytes = QueryWorkingSet(process.Get());

                FILETIME creation{};
                FILETIME exit{};
                FILETIME kernel{};
                FILETIME user{};

                if (GetProcessTimes(process.Get(), &creation, &exit, &kernel, &user))
                {
                    const auto creationTime = FileTimeToUInt64(creation);
                    const auto totalTime = FileTimeToUInt64(kernel) + FileTimeToUInt64(user);

                    if (const auto iterator = previousCpuSamples_.find(processId);
                        iterator != previousCpuSamples_.end() &&
                        iterator->second.creationTime == creationTime &&
                        wallTimeMs > iterator->second.wallTimeMs &&
                        totalTime >= iterator->second.totalTime)
                    {
                        const auto processDelta = totalTime - iterator->second.totalTime;
                        const auto wallDeltaMs = wallTimeMs - iterator->second.wallTimeMs;
                        const double capacity100ns =
                            static_cast<double>(wallDeltaMs) * 10000.0 * processorCount;

                        if (capacity100ns > 0.0)
                        {
                            info.cpuPercent = std::clamp(
                                static_cast<double>(processDelta) / capacity100ns * 100.0,
                                0.0,
                                100.0);
                        }
                    }

                    previousCpuSamples_[processId] = CpuSample{
                        creationTime,
                        totalTime,
                        wallTimeMs
                    };
                }
            }

            processes.push_back(std::move(info));
        }
        while (Process32NextW(snapshot.Get(), &entry));

        const DWORD error = GetLastError();
        if (error != ERROR_NO_MORE_FILES)
        {
            ThrowWin32Error(error, "Process32NextW");
        }

        std::erase_if(
            previousCpuSamples_,
            [&seenProcessIds](const auto& item)
            {
                return !seenProcessIds.contains(item.first);
            });

        return processes;
    }
}
