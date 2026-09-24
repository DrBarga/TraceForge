#pragma once

#include "ProcessEnumerator.h"
#include "ProcessWatcher.h"

#include <Windows.h>
#include <string>

namespace traceforge::agent
{
    class NamedPipeServer final
    {
    public:
        explicit NamedPipeServer(std::wstring pipeName = L"TraceForge.Agent.v1");
        void Run();

    private:
        [[nodiscard]] bool ReadLine(HANDLE pipe, std::string& line) const;
        void WriteLine(HANDLE pipe, const std::string& line) const;
        void ServeClient(HANDLE pipe, bool& keepRunning);

        ProcessEnumerator processEnumerator_;
        ProcessWatcher processWatcher_;
        std::wstring pipePath_;
    };
}
