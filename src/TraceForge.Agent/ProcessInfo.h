#pragma once

#include <cstdint>
#include <string>

namespace traceforge::agent
{
    struct ProcessInfo final
    {
        std::uint32_t processId{};
        std::wstring name;
        std::wstring executablePath;
        double cpuPercent{};
        std::uint64_t workingSetBytes{};
        std::uint32_t threadCount{};
        bool accessible{};
        std::uint32_t accessError{};
    };
}
