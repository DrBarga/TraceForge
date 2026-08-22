#pragma once

#include <cstdint>
#include <string>

namespace traceforge::agent
{
    struct ProcessInfo final
    {
        std::uint32_t processId;
        std::wstring name;
    };
}