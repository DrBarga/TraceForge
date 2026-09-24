#include "ProcessWatcher.h"

#include <Windows.h>

#include <chrono>
#include <stdexcept>
#include <system_error>
#include <utility>

namespace
{
    [[noreturn]] void ThrowWin32Error(DWORD error, const char* operation)
    {
        throw std::system_error(static_cast<int>(error), std::system_category(), operation);
    }

    [[nodiscard]] std::int64_t UnixMillisecondsNow()
    {
        const auto now = std::chrono::system_clock::now();
        return std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch()).count();
    }
}

namespace traceforge::agent
{
    void ProcessWatcher::Watch(std::uint32_t processId)
    {
        if (processId == 0)
        {
            throw std::invalid_argument("Process ID 0 cannot be watched.");
        }

        UniqueHandle process(OpenProcess(
            SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION,
            FALSE,
            processId));
        if (!process.IsValid())
        {
            ThrowWin32Error(GetLastError(), "OpenProcess(watch)");
        }

        process_ = std::move(process);
        processId_ = processId;
    }

    void ProcessWatcher::Stop() noexcept
    {
        process_.Reset();
        processId_ = 0;
    }

    std::optional<ProcessExit> ProcessWatcher::Poll()
    {
        if (!process_.IsValid())
        {
            return std::nullopt;
        }

        const DWORD waitResult = WaitForSingleObject(process_.Get(), 0);
        if (waitResult == WAIT_TIMEOUT)
        {
            return std::nullopt;
        }
        if (waitResult == WAIT_FAILED)
        {
            ThrowWin32Error(GetLastError(), "WaitForSingleObject(watch)");
        }

        DWORD exitCode = 0;
        if (!GetExitCodeProcess(process_.Get(), &exitCode))
        {
            ThrowWin32Error(GetLastError(), "GetExitCodeProcess(watch)");
        }

        const ProcessExit result{processId_, exitCode, UnixMillisecondsNow()};
        Stop();
        return result;
    }
}
