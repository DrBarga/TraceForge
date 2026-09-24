#pragma once

#include <cstdint>
#include <string>

namespace traceforge::agent
{
    struct ProcessInfo final
    {
        std::uint32_t processId{};
        std::uint32_t parentProcessId{};
        std::int64_t startTimeUnixMs{};
        std::wstring name;
        std::wstring executablePath;
        double cpuPercent{};
        std::uint64_t workingSetBytes{};
        std::uint32_t threadCount{};
        bool accessible{};
        std::uint32_t accessError{};
        bool pathAvailable{};
        std::uint32_t pathError{};
        bool cpuAvailable{};
        std::uint32_t cpuError{};
        bool memoryAvailable{};
        std::uint32_t memoryError{};
    };
}
