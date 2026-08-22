#include "AgentApplication.h"

#include <exception>
#include <iostream>

int main()
{
    try
    {
        const traceforge::agent::AgentApplication application;
        return static_cast<int>(application.Run());
    }
    catch (const std::exception& exception)
    {
        std::cerr << "TraceForge Agent fatal error: "
            << exception.what()
            << '\n';

        return static_cast<int>(
            traceforge::agent::ExitCode::UnhandledException);
    }
    catch (...)
    {
        std::cerr << "TraceForge Agent fatal error: unknown exception\n";

        return static_cast<int>(
            traceforge::agent::ExitCode::UnhandledException);
    }
}