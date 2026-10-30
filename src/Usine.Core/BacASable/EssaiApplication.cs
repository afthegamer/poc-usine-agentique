using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Usine.Core.Etat;
using Usine.Core.Orchestration;

namespace Usine.Core.BacASable;

public static class EssaiApplication
{
    public static (string Source, string Volume, string Libelle) CibleEssai(DepotDemandes depot, string? projet, string id)
    {
        if (projet is not null && id.Length > 0)
        {
            throw new ArgumentException("Indique soit un projet de l'usine (--projet <nom>), soit une demande (<id>), pas les deux.");
        }

        if (projet is not null)
        {
            return (Projets.Chemin(depot, projet), $"usine-app-{projet}", $"Projet « {projet} »");
        }

        if (id.Length == 0)
        {
            throw new ArgumentException("Indique le projet de l'usine (--projet <nom>, voir « usine projets ») ou la demande (<id>) à essayer.");
        }

        var demande = depot.Charger(id);
        if (demande.Livrable?.RepertoireCode is not { } code || !Directory.Exists(code))
        {
            throw new InvalidOperationException($"La demande {id} n'a pas encore de livrable à essayer.");
        }

        return (code, $"usine-app-{id}", $"Livrable de la demande {id}");
    }

    public static int PortEssai(string? valeur, int parDefaut)
    {
        if (valeur is null)
        {
            return parDefaut;
        }

        return int.TryParse(valeur, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
            ? port
            : throw new ArgumentException($"Port invalide : « {valeur} » (attendu : un nombre de 1 à 65535).");
    }

    public static IReadOnlyList<string> ArgumentsEssai(string source, string volume, string image, int portHote, int portConteneur, bool interactif) =>
        ["run", "--rm", interactif ? "-it" : "-i", .. ArgumentsApplication(source, volume, image, portHote, portConteneur)];

    public static IReadOnlyList<string> ArgumentsEssaiDetache(string conteneur, string source, string volume, string image, int portHote, int portConteneur) =>
        ["run", "-d", "--rm", "--name", conteneur, "--label", $"usine.port={portHote.ToString(CultureInfo.InvariantCulture)}", .. ArgumentsApplication(source, volume, image, portHote, portConteneur)];

    public static bool PortLibre(int port, IPAddress? adresse = null)
    {
        try
        {
            var ecoute = new TcpListener(adresse ?? IPAddress.Any, port);
            ecoute.Start();
            ecoute.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public static bool PortLibreSurCePoste(int port) =>
        PortLibre(port) && PortLibre(port, IPAddress.Loopback) && (!Socket.OSSupportsIPv6 || PortLibre(port, IPAddress.IPv6Loopback))
        && IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().All(ecoute => ecoute.Port != port);

    private static IEnumerable<string> ArgumentsApplication(string source, string volume, string image, int portHote, int portConteneur) =>
    [
        "-p", $"{portHote.ToString(CultureInfo.InvariantCulture)}:{portConteneur.ToString(CultureInfo.InvariantCulture)}",
        "-v", $"{source}:/src:ro",
        "-v", $"{volume}:/workspace",
        image,
        "bash", "-c", $"cp -rf /src/. /workspace/ && cd /workspace && ./usine-build.sh && PORT={portConteneur.ToString(CultureInfo.InvariantCulture)} ./usine-start.sh",
    ];
}
