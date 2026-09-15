#pragma once

#include "ExitCode.h"

namespace traceforge::agent
{
    class AgentApplication final
    {
    public:
        [[nodiscard]] ExitCode Run() const;
    };
}
