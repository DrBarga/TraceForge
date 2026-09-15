#include "NamedPipeServer.h"
#include "JsonProtocol.h"
#include "UniqueHandle.h"

#include <chrono>
#include <stdexcept>
#include <system_error>

namespace
{
    constexpr wchar_t PipePath[] = LR"(\\.\pipe\TraceForge.Agent.v1)";
    constexpr std::size_t MaxRequestLength = 64 * 1024;

    [[noreturn]] void ThrowWin32Error(DWORD error, const char* operation)
    {
        throw std::system_error(
            static_cast<int>(error),
            std::system_category(),
            operation);
    }

    [[nodiscard]] std::int64_t UnixMillisecondsNow()
    {
        const auto now = std::chrono::system_clock::now();
        return std::chrono::duration_cast<std::chrono::milliseconds>(
            now.time_since_epoch()).count();
    }
}

namespace traceforge::agent
{
    void NamedPipeServer::Run()
    {
        bool keepRunning = true;

        while (keepRunning)
        {
            const UniqueHandle pipe(
                CreateNamedPipeW(
                    PipePath,
                    PIPE_ACCESS_DUPLEX,
                    PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                    1,
                    1024 * 1024,
                    1024 * 1024,
                    0,
                    nullptr));

            if (!pipe.IsValid())
            {
                ThrowWin32Error(GetLastError(), "CreateNamedPipeW");
            }

            const BOOL connected = ConnectNamedPipe(pipe.Get(), nullptr);
            if (!connected)
            {
                const DWORD error = GetLastError();
                if (error != ERROR_PIPE_CONNECTED)
                {
                    ThrowWin32Error(error, "ConnectNamedPipe");
                }
            }

            ServeClient(pipe.Get(), keepRunning);
            FlushFileBuffers(pipe.Get());
            DisconnectNamedPipe(pipe.Get());
        }
    }

    bool NamedPipeServer::ReadLine(HANDLE pipe, std::string& line) const
    {
        line.clear();

        while (line.size() < MaxRequestLength)
        {
            char character{};
            DWORD bytesRead{};

            if (!ReadFile(pipe, &character, 1, &bytesRead, nullptr))
            {
                const DWORD error = GetLastError();
                if (error == ERROR_BROKEN_PIPE || error == ERROR_NO_DATA)
                {
                    return false;
                }

                ThrowWin32Error(error, "ReadFile");
            }

            if (bytesRead == 0)
            {
                return false;
            }

            if (character == '\n')
            {
                return true;
            }

            if (character != '\r')
            {
                line.push_back(character);
            }
        }

        throw std::runtime_error("TraceForge Agent request exceeded the maximum length.");
    }

    void NamedPipeServer::WriteLine(HANDLE pipe, const std::string& line) const
    {
        std::string payload = line;
        payload.push_back('\n');

        std::size_t offset = 0;
        while (offset < payload.size())
        {
            DWORD bytesWritten{};
            const DWORD remaining = static_cast<DWORD>(payload.size() - offset);

            if (!WriteFile(
                    pipe,
                    payload.data() + offset,
                    remaining,
                    &bytesWritten,
                    nullptr))
            {
                const DWORD error = GetLastError();
                if (error == ERROR_BROKEN_PIPE || error == ERROR_NO_DATA)
                {
                    return;
                }

                ThrowWin32Error(error, "WriteFile");
            }

            offset += bytesWritten;
        }
    }

    void NamedPipeServer::ServeClient(HANDLE pipe, bool& keepRunning)
    {
        std::string request;

        while (keepRunning && ReadLine(pipe, request))
        {
            try
            {
                const auto type = json::ExtractType(request);

                if (type == "ping")
                {
                    WriteLine(pipe, json::BuildPongResponse());
                    continue;
                }

                if (type == "snapshot")
                {
                    auto processes = processEnumerator_.Enumerate();
                    WriteLine(
                        pipe,
                        json::BuildSnapshotResponse(
                            processes,
                            UnixMillisecondsNow()));
                    continue;
                }

                if (type == "shutdown")
                {
                    WriteLine(pipe, json::BuildShutdownResponse());
                    keepRunning = false;
                    return;
                }

                WriteLine(pipe, json::BuildErrorResponse("Unsupported request type."));
            }
            catch (const std::exception& exception)
            {
                WriteLine(pipe, json::BuildErrorResponse(exception.what()));
            }
        }
    }
}
