#pragma once

#include "ProcessInfo.h"

#include <vector>

namespace traceforge::agent
{
    class ProcessEnumerator final
    {
    public:
        [[nodiscard]] std::vector<ProcessInfo> Enumerate() const;
    };
}