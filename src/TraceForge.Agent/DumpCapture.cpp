#include "DumpCapture.h"
#include "UniqueHandle.h"

#include <DbgHelp.h>
#include <ShlObj.h>

#include <filesystem>
#include <format>
#include <stdexcept>
#include <system_error>

namespace
{
    [[noreturn]] void ThrowWin32Error(DWORD error, const char* operation)
    {
        throw std::system_error(
            static_cast<int>(error),
            std::system_category(),
            operation);
    }

    [[nodiscard]] std::filesystem::path GetDumpDirectory()
    {
        PWSTR localAppData = nullptr;
        const HRESULT result = SHGetKnownFolderPath(
            FOLDERID_LocalAppData,
            KF_FLAG_CREATE,
            nullptr,
            &localAppData);
        if (FAILED(result))
        {
            throw std::system_error(
                static_cast<int>(result),
                std::system_category(),
                "SHGetKnownFolderPath");
        }

        const std::filesystem::path directory =
            std::filesystem::path(localAppData) / L"TraceForge" / L"Dumps";
        CoTaskMemFree(localAppData);
        std::filesystem::create_directories(directory);
        return directory;
    }

    [[nodiscard]] std::wstring BuildDumpName(std::uint32_t processId)
    {
        SYSTEMTIME time{};
        GetSystemTime(&time);
        return std::format(
            L"TraceForge-{}-{:04}{:02}{:02}-{:02}{:02}{:02}-{:03}.dmp",
            processId,
            time.wYear,
            time.wMonth,
            time.wDay,
            time.wHour,
            time.wMinute,
            time.wSecond,
            time.wMilliseconds);
    }
}

namespace traceforge::agent
{
    std::wstring DumpCapture::Capture(std::uint32_t processId)
    {
        if (processId == 0)
        {
            throw std::invalid_argument("Process ID 0 cannot be dumped.");
        }

        const UniqueHandle process(OpenProcess(
            PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_DUP_HANDLE,
            FALSE,
            processId));
        if (!process.IsValid())
        {
            ThrowWin32Error(GetLastError(), "OpenProcess(MiniDump)");
        }

        const auto dumpPath = GetDumpDirectory() / BuildDumpName(processId);
        UniqueHandle dumpFile(CreateFileW(
            dumpPath.c_str(),
            GENERIC_WRITE,
            0,
            nullptr,
            CREATE_NEW,
            FILE_ATTRIBUTE_NORMAL,
            nullptr));
        if (!dumpFile.IsValid())
        {
            ThrowWin32Error(GetLastError(), "CreateFileW(MiniDump)");
        }

        constexpr MINIDUMP_TYPE dumpType = static_cast<MINIDUMP_TYPE>(
            MiniDumpWithThreadInfo |
            MiniDumpWithUnloadedModules |
            MiniDumpWithProcessThreadData);

        if (!MiniDumpWriteDump(
                process.Get(),
                processId,
                dumpFile.Get(),
                dumpType,
                nullptr,
                nullptr,
                nullptr))
        {
            const DWORD error = GetLastError();
            dumpFile.Reset();
            std::filesystem::remove(dumpPath);
            ThrowWin32Error(error, "MiniDumpWriteDump");
        }

        return dumpPath.wstring();
    }
}
