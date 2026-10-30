using System.Globalization;
using Usine.Api.Dto;
using Usine.Core;
using Usine.Core.BacASable;

namespace Usine.Api;

internal sealed class EssaisApplications(ContexteUsine usine, ICommandesDocker docker)
{
    private readonly SemaphoreSlim verrou = new(1, 1);

    public async Task<EssaiDto> LancerAsync(string projet, LancementEssaiDto? corps, CancellationToken ct)
    {
        var bac = usine.Config.BacASable;
        var (source, volume, _) = EssaiApplication.CibleEssai(usine.Depot, projet, "");
        int? portDemande = corps?.Port is { } port ? EssaiApplication.PortEssai(port.ToString(CultureInfo.InvariantCulture), bac.Port) : null;
        await verrou.WaitAsync(ct);
        try
        {
            if (await EtatConteneurAsync(projet, ct) is { EnCours: true } enCours)
            {
                return enCours;
            }

            if (!(await docker.ExecuterAsync(["version", "--format", "{{.Server.Version}}"], ct)).Reussi)
            {
                throw new InvalidOperationException("Docker ne répond pas : démarre Docker (Docker Desktop sous Windows), attends qu'il soit prêt, puis réessaie.");
            }

            if (!(await docker.ExecuterAsync(["image", "inspect", bac.Image], ct)).Reussi)
            {
                throw new InvalidOperationException($"L'image {bac.Image} est absente : construis-la d'abord avec « usine image ».");
            }

            var portHote = portDemande ?? PremierPortLibre(bac.Port);
            if (portDemande is not null && !EssaiApplication.PortLibreSurCePoste(portHote))
            {
                throw new InvalidOperationException($"Le port {portHote} est déjà pris sur ce poste : choisis-en un autre, ou laisse l'usine en choisir un libre.");
            }

            await docker.ExecuterAsync(["rm", "-f", NomConteneur(projet)], ct);
            if (!(await docker.ExecuterAsync(EssaiApplication.ArgumentsEssaiDetache(NomConteneur(projet), source, volume, bac.Image, portHote, bac.Port), ct)).Reussi)
            {
                throw new InvalidOperationException($"Docker n'a pas pu lancer l'application du projet « {projet} ».");
            }

            return new EssaiDto(true, Adresse(portHote));
        }
        finally
        {
            verrou.Release();
        }
    }

    public async Task<EssaiDto> EtatAsync(string projet, CancellationToken ct)
    {
        EssaiApplication.CibleEssai(usine.Depot, projet, "");
        return await EtatConteneurAsync(projet, ct);
    }

    public async Task<EssaiDto> ArreterAsync(string projet, CancellationToken ct)
    {
        EssaiApplication.CibleEssai(usine.Depot, projet, "");
        await verrou.WaitAsync(ct);
        try
        {
            await docker.ExecuterAsync(["rm", "-f", NomConteneur(projet)], ct);
            return await EtatConteneurAsync(projet, ct) is { EnCours: true }
                ? throw new InvalidOperationException($"Docker n'a pas pu arrêter l'application du projet « {projet} ».")
                : new EssaiDto(false, null);
        }
        finally
        {
            verrou.Release();
        }
    }

    private async Task<EssaiDto> EtatConteneurAsync(string projet, CancellationToken ct)
    {
        var inspection = await docker.ExecuterAsync(["container", "inspect", "-f", "{{.State.Running}}|{{index .Config.Labels \"usine.port\"}}", NomConteneur(projet)], ct);
        return inspection.Reussi && inspection.Sortie.Trim().Split('|') is ["true", var valeur] && int.TryParse(valeur, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            ? new EssaiDto(true, Adresse(port))
            : new EssaiDto(false, null);
    }

    private static string NomConteneur(string projet) => $"usine-essai-{projet}";

    private static int PremierPortLibre(int depart)
    {
        for (var port = depart; port <= 65535; port++)
        {
            if (EssaiApplication.PortLibreSurCePoste(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException($"Aucun port n'est libre sur ce poste à partir du port {depart}.");
    }

    private static string Adresse(int port) => $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}";
}
