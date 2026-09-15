#pragma once

#include "ProcessInfo.h"

#include <cstdint>
#include <unordered_map>
#include <vector>

namespace traceforge::agent
{
    class ProcessEnumerator final
    {
    public:
        [[nodiscard]] std::vector<ProcessInfo> Enumerate();

    private:
        struct CpuSample final
        {
            std::uint64_t creationTime{};
            std::uint64_t totalTime{};
            std::uint64_t wallTimeMs{};
        };

        std::unordered_map<std::uint32_t, CpuSample> previousCpuSamples_;
    };
}
