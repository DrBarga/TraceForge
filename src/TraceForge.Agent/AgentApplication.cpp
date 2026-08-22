#include "AgentApplication.h"
#include "ProcessEnumerator.h"

#include <iomanip>
#include <iostream>

namespace traceforge::agent
{
    ExitCode AgentApplication::Run() const
    {
        const ProcessEnumerator enumerator;
        const auto processes = enumerator.Enumerate();

        std::wcout
            << std::left
            << std::setw(12)
            << L"PID"
            << L"PROCESS\n";

        std::wcout
            << L"----------------------------------------\n";

        for (const auto& process : processes)
        {
            std::wcout
                << std::left
                << std::setw(12)
                << process.processId
                << process.name
                << L'\n';
        }

        std::wcout
            << L"\nProcesses discovered: "
            << processes.size()
            << L'\n';

        return ExitCode::Success;
    }
}