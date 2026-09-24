#pragma once

#include "ExitCode.h"
#include <string_view>

namespace traceforge::agent
{
    class AgentApplication final
    {
    public:
        [[nodiscard]] ExitCode Run(std::wstring_view pipeName) const;
    };
}
