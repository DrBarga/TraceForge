#pragma once

#include <cstdint>
#include <string>

namespace traceforge::agent
{
    class DumpCapture final
    {
    public:
        [[nodiscard]] static std::wstring Capture(std::uint32_t processId);
    };
}
