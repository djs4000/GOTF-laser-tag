using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LaserTag.Defusal.Domain;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Validates remote endpoints against a configured CIDR allowlist.
/// </summary>
public sealed class CidrAllowlistService
{
    private readonly ILogger<CidrAllowlistService> _logger;
    private readonly HttpOptions _options;
    private readonly List<(IPAddress network, IPAddress mask)> _rules = new();

    public CidrAllowlistService(IOptions<HttpOptions> options, ILogger<CidrAllowlistService> logger)
    {
        _logger = logger;
        _options = options.Value;
        foreach (var cidr in _options.AllowedCidrs)
        {
            if (TryParseCidr(cidr, out var network, out var mask))
            {
                _rules.Add((network, mask));
            }
            else
            {
                _logger.LogWarning("Invalid CIDR {Cidr} skipped", cidr);
            }
        }
    }

    /// <summary>
    /// Determines whether the given IP address is permitted.
    /// </summary>
    public bool IsAllowed(IPAddress address)
    {
        if (_rules.Count == 0)
        {
            // No allowlist entries means all are denied by default.
            return false;
        }

        foreach (var (network, mask) in _rules)
        {
            if (IsInSubnet(address, network, mask))
            {
                return true;
            }
        }

        _logger.LogWarning("IP address {Address} rejected by allowlist", address);
        return false;
    }

    private static bool TryParseCidr(string cidr, out IPAddress network, out IPAddress mask)
    {
        network = IPAddress.None;
        mask = IPAddress.None;
        var parts = cidr.Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[0], out network))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        if (network.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        if (prefixLength < 0 || prefixLength > 32)
        {
            return false;
        }

        mask = PrefixToMask(prefixLength);
        return true;
    }

    private static bool IsInSubnet(IPAddress address, IPAddress network, IPAddress mask)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        for (var i = 0; i < maskBytes.Length; i++)
        {
            if ((addressBytes[i] & maskBytes[i]) != (networkBytes[i] & maskBytes[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static IPAddress PrefixToMask(int prefixLength)
    {
        uint mask = uint.MaxValue << (32 - prefixLength);
        var bytes = BitConverter.GetBytes(mask);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        return new IPAddress(bytes);
    }
}
