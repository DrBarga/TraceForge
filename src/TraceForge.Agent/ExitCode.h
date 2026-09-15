#pragma once

namespace traceforge::agent
{
    enum class ExitCode : int
    {
        Success = 0,
        UnhandledException = 1
    };
}
