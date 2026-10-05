using Game;
using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace RouteFilter.Systems;

// Vanilla turns highlighted static objects into OutlineOnly batches. These
// runtime signs must retain their face when hit; selection/tooltip ownership
// remains on the marker, but its decorative native outline is suppressed.
public sealed class RoadRestrictionSignVisibilitySystem : GameSystemBase
{
    private EntityQuery m_Highlighted;
    protected override void OnCreate()
    {
        base.OnCreate();
        m_Highlighted=GetEntityQuery(new EntityQueryDesc {
            All=new[]{ComponentType.ReadOnly<RoadRestrictionSignOwner>(),ComponentType.ReadOnly<Highlighted>()},
            None=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Temp>()}
        });
        RequireForUpdate(m_Highlighted);
    }
    protected override void OnUpdate()
    {
        EntityManager.AddComponent<BatchesUpdated>(m_Highlighted);
        EntityManager.RemoveComponent<Highlighted>(m_Highlighted);
    }
}
