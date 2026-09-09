using UnityEngine;

namespace TradeWinds
{
    public sealed class HelmInteraction : MonoBehaviour
    {
        public bool TryBegin(ShipActor actor)
        {
            return actor.NearHelm && actor.Interaction.HeldItem == null && actor.HomeShip.TryTakeHelm(actor);
        }
    }
}
