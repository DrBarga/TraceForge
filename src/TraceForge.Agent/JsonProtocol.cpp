#include "JsonProtocol.h"

#include <Windows.h>

#include <iomanip>
#include <sstream>

namespace
{
    [[nodiscard]] std::string ToUtf8(std::wstring_view value)
    {
        if (value.empty())
        {
            return {};
        }

        const int size = WideCharToMultiByte(
            CP_UTF8,
            0,
            value.data(),
            static_cast<int>(value.size()),
            nullptr,
            0,
            nullptr,
            nullptr);

        if (size <= 0)
        {
            return {};
        }

        std::string result(static_cast<std::size_t>(size), '\0');
        WideCharToMultiByte(
            CP_UTF8,
            0,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            size,
            nullptr,
            nullptr);
        return result;
    }

    [[nodiscard]] std::string EscapeJson(std::string_view value)
    {
        std::string result;
        result.reserve(value.size() + 16);

        for (const unsigned char character : value)
        {
            switch (character)
            {
            case '"': result += "\\\""; break;
            case '\\': result += "\\\\"; break;
            case '\b': result += "\\b"; break;
            case '\f': result += "\\f"; break;
            case '\n': result += "\\n"; break;
            case '\r': result += "\\r"; break;
            case '\t': result += "\\t"; break;
            default:
                if (character < 0x20)
                {
                    std::ostringstream escaped;
                    escaped << "\\u"
                            << std::hex
                            << std::setw(4)
                            << std::setfill('0')
                            << static_cast<int>(character);
                    result += escaped.str();
                }
                else
                {
                    result.push_back(static_cast<char>(character));
                }
                break;
            }
        }

        return result;
    }
}

namespace traceforge::agent::json
{
    std::string ExtractType(std::string_view request)
    {
        constexpr std::string_view key = "\"type\"";
        const auto keyPosition = request.find(key);
        if (keyPosition == std::string_view::npos)
        {
            return {};
        }

        const auto colonPosition = request.find(':', keyPosition + key.size());
        if (colonPosition == std::string_view::npos)
        {
            return {};
        }

        const auto quoteStart = request.find('"', colonPosition + 1);
        if (quoteStart == std::string_view::npos)
        {
            return {};
        }

        const auto quoteEnd = request.find('"', quoteStart + 1);
        if (quoteEnd == std::string_view::npos)
        {
            return {};
        }

        return std::string(request.substr(quoteStart + 1, quoteEnd - quoteStart - 1));
    }

    std::string BuildPongResponse()
    {
        return R"({"type":"pong"})";
    }

    std::string BuildShutdownResponse()
    {
        return R"({"type":"shutdown"})";
    }

    std::string BuildErrorResponse(std::string_view message)
    {
        return "{\"type\":\"error\",\"message\":\"" + EscapeJson(message) + "\"}";
    }

    std::string BuildSnapshotResponse(
        const std::vector<ProcessInfo>& processes,
        std::int64_t timestampUnixMs)
    {
        std::ostringstream output;
        output << std::fixed << std::setprecision(2);
        output << "{\"type\":\"snapshot\",\"timestampUnixMs\":"
               << timestampUnixMs
               << ",\"processes\":[";

        bool first = true;
        for (const auto& process : processes)
        {
            if (!first)
            {
                output << ',';
            }
            first = false;

            output << "{\"pid\":" << process.processId
                   << ",\"name\":\"" << EscapeJson(ToUtf8(process.name)) << '"'
                   << ",\"path\":\"" << EscapeJson(ToUtf8(process.executablePath)) << '"'
                   << ",\"cpuPercent\":" << process.cpuPercent
                   << ",\"workingSetBytes\":" << process.workingSetBytes
                   << ",\"threadCount\":" << process.threadCount
                   << ",\"accessible\":" << (process.accessible ? "true" : "false")
                   << ",\"accessError\":" << process.accessError
                   << '}';
        }

        output << "]}";
        return output.str();
    }
}
