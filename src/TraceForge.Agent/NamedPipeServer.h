#pragma once

#include "ProcessEnumerator.h"

#include <Windows.h>
#include <string>

namespace traceforge::agent
{
    class NamedPipeServer final
    {
    public:
        void Run();

    private:
        [[nodiscard]] bool ReadLine(HANDLE pipe, std::string& line) const;
        void WriteLine(HANDLE pipe, const std::string& line) const;
        void ServeClient(HANDLE pipe, bool& keepRunning);

        ProcessEnumerator processEnumerator_;
    };
}
