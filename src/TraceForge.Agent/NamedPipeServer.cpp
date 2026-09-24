#include "NamedPipeServer.h"
#include "DumpCapture.h"
#include "JsonProtocol.h"
#include "ProcessInspection.h"
#include "UniqueHandle.h"

#include <chrono>
#include <Sddl.h>
#include <stdexcept>
#include <system_error>
#include <vector>

namespace
{
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

    class PipeSecurity final
    {
    public:
        PipeSecurity()
        {
            const traceforge::agent::UniqueHandle token = OpenCurrentProcessToken();

            DWORD tokenInformationSize = 0;
            GetTokenInformation(token.Get(), TokenUser, nullptr, 0, &tokenInformationSize);
            if (GetLastError() != ERROR_INSUFFICIENT_BUFFER)
            {
                ThrowWin32Error(GetLastError(), "GetTokenInformation(size)");
            }

            std::vector<std::byte> tokenInformation(tokenInformationSize);
            if (!GetTokenInformation(
                    token.Get(),
                    TokenUser,
                    tokenInformation.data(),
                    tokenInformationSize,
                    &tokenInformationSize))
            {
                ThrowWin32Error(GetLastError(), "GetTokenInformation");
            }

            const auto tokenUser = reinterpret_cast<const TOKEN_USER*>(tokenInformation.data());
            LPWSTR sidText = nullptr;
            if (!ConvertSidToStringSidW(tokenUser->User.Sid, &sidText))
            {
                ThrowWin32Error(GetLastError(), "ConvertSidToStringSidW");
            }

            const std::wstring securityDefinition =
                L"D:P(A;;GA;;;" + std::wstring(sidText) + L")(A;;GA;;;SY)";
            LocalFree(sidText);

            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
                    securityDefinition.c_str(),
                    SDDL_REVISION_1,
                    &securityDescriptor_,
                    nullptr))
            {
                ThrowWin32Error(
                    GetLastError(),
                    "ConvertStringSecurityDescriptorToSecurityDescriptorW");
            }

            attributes_.nLength = sizeof(attributes_);
            attributes_.lpSecurityDescriptor = securityDescriptor_;
            attributes_.bInheritHandle = FALSE;
        }

        ~PipeSecurity()
        {
            if (securityDescriptor_ != nullptr)
            {
                LocalFree(securityDescriptor_);
            }
        }

        PipeSecurity(const PipeSecurity&) = delete;
        PipeSecurity& operator=(const PipeSecurity&) = delete;

        [[nodiscard]] SECURITY_ATTRIBUTES* Attributes() noexcept
        {
            return &attributes_;
        }

    private:
        [[nodiscard]] static traceforge::agent::UniqueHandle OpenCurrentProcessToken()
        {
            HANDLE token = nullptr;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
            {
                ThrowWin32Error(GetLastError(), "OpenProcessToken");
            }

            return traceforge::agent::UniqueHandle(token);
        }

        PSECURITY_DESCRIPTOR securityDescriptor_ = nullptr;
        SECURITY_ATTRIBUTES attributes_{};
    };
}

namespace traceforge::agent
{
    NamedPipeServer::NamedPipeServer(std::wstring pipeName)
        : pipePath_(LR"(\\.\pipe\)" + pipeName)
    {
        if (pipeName.empty() || pipeName.size() > 128 ||
            pipeName.find_first_not_of(L"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-") != std::wstring::npos)
        {
            throw std::invalid_argument("Invalid Agent pipe name.");
        }
    }

    void NamedPipeServer::Run()
    {
        bool keepRunning = true;
        PipeSecurity pipeSecurity;

        while (keepRunning)
        {
            const UniqueHandle pipe(
                CreateNamedPipeW(
                    pipePath_.c_str(),
                    PIPE_ACCESS_DUPLEX,
                    PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                    1,
                    1024 * 1024,
                    1024 * 1024,
                    0,
                    pipeSecurity.Attributes()));

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
                const auto parsed = json::ParseRequest(request);
                const auto& type = parsed.type;

                if (type == "ping")
                {
                    WriteLine(pipe, json::BuildPongResponse());
                    continue;
                }

                if (type == "snapshot")
                {
                    const auto processExit = processWatcher_.Poll();
                    auto processes = processEnumerator_.Enumerate();
                    WriteLine(
                        pipe,
                        json::BuildSnapshotResponse(
                            processes,
                            UnixMillisecondsNow(),
                            processExit));
                    continue;
                }

                if (type == "watch")
                {
                    const auto processId = parsed.processId;
                    if (!processId.has_value())
                    {
                        WriteLine(pipe, json::BuildErrorResponse("A valid process ID is required."));
                        continue;
                    }

                    processWatcher_.Watch(*processId);
                    WriteLine(pipe, json::BuildWatchResponse(*processId));
                    continue;
                }

                if (type == "inspect")
                {
                    const auto processId = parsed.processId;
                    if (!processId.has_value() || *processId == 0)
                    {
                        WriteLine(pipe, json::BuildErrorResponse("A valid process ID is required."));
                        continue;
                    }

                    WriteLine(pipe, json::BuildInspectionResponse(ProcessInspector::Inspect(*processId)));
                    continue;
                }

                if (type == "unwatch")
                {
                    processWatcher_.Stop();
                    WriteLine(pipe, json::BuildUnwatchResponse());
                    continue;
                }

                if (type == "shutdown")
                {
                    WriteLine(pipe, json::BuildShutdownResponse());
                    keepRunning = false;
                    return;
                }

                if (type == "captureDump")
                {
                    const auto processId = parsed.processId;
                    if (!processId.has_value())
                    {
                        WriteLine(pipe, json::BuildErrorResponse("A valid process ID is required."));
                        continue;
                    }

                    const auto path = DumpCapture::Capture(*processId);
                    WriteLine(pipe, json::BuildDumpResponse(path));
                    continue;
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
