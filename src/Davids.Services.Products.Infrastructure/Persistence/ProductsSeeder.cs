using Davids.Services.Products.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Davids.Services.Products.Infrastructure.Persistence;

public static class ProductsSeeder
{
    public static readonly Guid YubiKeyProductId = Guid.Parse("11111111-1111-4111-8111-111111111101");
    public static readonly Guid MechanicalKeyboardProductId = Guid.Parse("11111111-1111-4111-8111-111111111102");
    public static readonly Guid ThunderboltDockProductId = Guid.Parse("11111111-1111-4111-8111-111111111103");

    public static async Task SeedDefaultCatalogAsync(ProductsDbContext db, CancellationToken ct = default)
    {
        if (await db.Products.AnyAsync(ct))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var defaults = new[]
        {
            new Product(
                YubiKeyProductId,
                "SEC-FIDO2-KEY",
                "FIDO2 Hardware Security Key (USB-C / NFC)",
                "Phishing-resistant FIDO2/WebAuthn & OpenPGP hardware security token for zero-trust workstation and CI signing.",
                "Security Hardware",
                54.00m,
                "GBP",
                120,
                true,
                now),
            new Product(
                MechanicalKeyboardProductId,
                "DEV-KB-75PRO",
                "Tactile 75% Developer Mechanical Keyboard",
                "Hot-swappable aluminum 75% wireless/USB-C mechanical keyboard with PBT keycaps and QMK/VIA firmware.",
                "Peripherals",
                149.00m,
                "GBP",
                45,
                true,
                now),
            new Product(
                ThunderboltDockProductId,
                "DEV-TB4-DOCK",
                "Thunderbolt 4 Pro 14-Port Workstation Dock",
                "Dual 4K 120Hz display output, 96W upstream power delivery, 2.5GbE Ethernet, and UHS-II SD reader.",
                "Workstation",
                239.00m,
                "GBP",
                30,
                true,
                now),
            new Product(
                Guid.Parse("11111111-1111-4111-8111-111111111104"),
                "MON-4K-27IPS",
                "27-Inch 4K UHD Low-Glare Coding Monitor",
                "IPS Black 2000:1 contrast panel with hardware KVM switch, 90W USB-C hub, and ergonomic height stand.",
                "Displays",
                429.00m,
                "GBP",
                22,
                true,
                now),
            new Product(
                Guid.Parse("11111111-1111-4111-8111-111111111105"),
                "AUD-ANC-HEADSET",
                "Studio ANC Wireless Engineering Headset",
                "Multipoint Bluetooth 5.3 + lossless USB-C dongle headset with beamforming mic boom for clear pairing calls.",
                "Audio",
                189.00m,
                "GBP",
                60,
                true,
                now),
            new Product(
                Guid.Parse("11111111-1111-4111-8111-111111111106"),
                "BK-ZERO-TRUST-NET",
                "Zero-Trust Distributed Systems in .NET (Hardcover)",
                "Practical architecture handbook covering OpenIddict, BFF cookies, YARP gateways, transactional outboxes, and Istio mTLS.",
                "Books",
                42.50m,
                "GBP",
                85,
                true,
                now)
        };

        await db.Products.AddRangeAsync(defaults, ct);
        await db.SaveChangesAsync(ct);
    }
}
