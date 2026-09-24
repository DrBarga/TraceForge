#include "AgentApplication.h"

#include <exception>
#include <iostream>

int wmain(int argumentCount, wchar_t* arguments[])
{
    try
    {
        constexpr std::wstring_view option = L"--pipe=";
        std::wstring_view pipeName = L"TraceForge.Agent.v1";
        if (argumentCount > 2 ||
            (argumentCount == 2 && std::wstring_view(arguments[1]).substr(0, option.size()) != option))
        {
            std::cerr << "Usage: TraceForge.Agent.exe [--pipe=NAME]\n";
            return static_cast<int>(traceforge::agent::ExitCode::UnhandledException);
        }
        if (argumentCount == 2)
        {
            pipeName = std::wstring_view(arguments[1]).substr(option.size());
        }

        const traceforge::agent::AgentApplication application;
        return static_cast<int>(application.Run(pipeName));
    }
    catch (const std::exception& exception)
    {
        std::cerr << "TraceForge Agent fatal error: " << exception.what() << '\n';
        return static_cast<int>(traceforge::agent::ExitCode::UnhandledException);
    }
    catch (...)
    {
        std::cerr << "TraceForge Agent fatal error: unknown exception\n";
        return static_cast<int>(traceforge::agent::ExitCode::UnhandledException);
    }
}
