#include "ProcessEnumerator.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#include <TlHelp32.h>

#include <system_error>

namespace
{
    class SnapshotHandle final
    {
    public:
        explicit SnapshotHandle(HANDLE handle) noexcept
            : handle_(handle)
        {}

        ~SnapshotHandle()
        {
            if (handle_ != INVALID_HANDLE_VALUE)
            {
                CloseHandle(handle_);
            }
        }

        SnapshotHandle(const SnapshotHandle&) = delete;
        SnapshotHandle& operator=(const SnapshotHandle&) = delete;

        [[nodiscard]] HANDLE Get() const noexcept
        {
            return handle_;
        }

    private:
        HANDLE handle_;
    };

    [[noreturn]] void ThrowWin32Error(
        DWORD error,
        const char* operation)
    {
        throw std::system_error(
            static_cast<int>(error),
            std::system_category(),
            operation);
    }
}

namespace traceforge::agent
{
    std::vector<ProcessInfo> ProcessEnumerator::Enumerate() const
    {
        const HANDLE rawSnapshot =
            CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

        if (rawSnapshot == INVALID_HANDLE_VALUE)
        {
            ThrowWin32Error(
                GetLastError(),
                "CreateToolhelp32Snapshot");
        }

        const SnapshotHandle snapshot(rawSnapshot);

        PROCESSENTRY32W entry{};
        entry.dwSize = sizeof(entry);

        if (!Process32FirstW(snapshot.Get(), &entry))
        {
            ThrowWin32Error(
                GetLastError(),
                "Process32FirstW");
        }

        std::vector<ProcessInfo> processes;

        do
        {
            processes.push_back(
                ProcessInfo{
                    .processId =
                        static_cast<std::uint32_t>(
                            entry.th32ProcessID),
                    .name = entry.szExeFile
                });
        } while (Process32NextW(snapshot.Get(), &entry));

        const DWORD error = GetLastError();

        if (error != ERROR_NO_MORE_FILES)
        {
            ThrowWin32Error(
                error,
                "Process32NextW");
        }

        return processes;
    }
}