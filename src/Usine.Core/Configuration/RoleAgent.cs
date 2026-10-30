namespace Usine.Core.Configuration;

public enum RoleAgent
{
    Po,
    QaAmont,
    Dev,
    Qa,
}

public static class RolesAgent
{
    public static string Libelle(RoleAgent role) => role switch
    {
        RoleAgent.Po => "PO",
        RoleAgent.QaAmont => "QA amont",
        RoleAgent.Dev => "Dev",
        RoleAgent.Qa => "QA",
        _ => role.ToString(),
    };
}
