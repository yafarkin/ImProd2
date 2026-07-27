using Game.Domain;

namespace Game.Web;

/// <summary>Единая карта роль → «домашний» маршрут — переиспользуется редиректом после логина (Блок 8.1) и главной страницей.</summary>
public static class RoleRouting
{
    public static string HomeRoute(ParticipantRole role) => role switch
    {
        ParticipantRole.Manager or ParticipantRole.Negotiator => "/team",
        ParticipantRole.Operator => "/operator",
        ParticipantRole.Facilitator => "/facilitator",
        ParticipantRole.Administrator => "/admin",
        _ => "/",
    };
}
