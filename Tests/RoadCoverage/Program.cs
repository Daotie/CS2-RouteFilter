using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Game.Vehicles;
using RouteFilter.Systems;
using Unity.Collections;
using Unity.Entities;

var checks = 0;
void Check(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
var world = new EntityManager();
var head = world.Create(1); var physical = world.Create(2); var forbidden = world.Create(3);
var allowed = world.Create(4); var target = world.Create(5); var otherTarget = world.Create(6);
world.Set(head, new PrefabRef { m_Prefab = allowed });
world.Set(physical, new PrefabRef { m_Prefab = forbidden });
world.Set(physical, new Controller { m_Controller = head });
var restrictions = new NativeParallelMultiHashMap<Entity, Entity>(); restrictions.Add(target, forbidden);
bool Match(Entity p, Entity h, Entity t, out Entity matched) => VehiclePrefabMatcher.TryMatch(p, h, t,
    new ComponentLookup<PrefabRef>(world), new BufferLookup<LayoutElement>(world), restrictions, out matched);
Check(Match(physical, head, target, out var matched) && matched == forbidden, "physical-only candidate matches exact prefab");
Check(VehiclePrefabMatcher.StillMatches(physical, head, matched, world), "physical-only receipt survives without LayoutElement");
Check(!Match(head, head, target, out _), "allowed canonical alone is not forbidden");
Check(!Match(physical, head, otherTarget, out _), "different target never inherits restriction");
world.Set(physical, new Controller { m_Controller = otherTarget });
Check(!VehiclePrefabMatcher.StillMatches(physical, head, matched, world), "detached physical invalidates receipt");
world.Set(physical, new Controller { m_Controller = head });
world.Set(physical, new PrefabRef { m_Prefab = allowed });
Check(!VehiclePrefabMatcher.StillMatches(physical, head, matched, world), "changed physical prefab invalidates receipt");
world.Set(physical, new PrefabRef { m_Prefab = forbidden }); world.Set(physical, new Deleted());
Check(!VehiclePrefabMatcher.StillMatches(physical, head, matched, world), "deleted physical cannot authorize termination");
var temp = world.Create(7); world.Set(temp, new PrefabRef { m_Prefab = forbidden });
world.Set(temp, new Controller { m_Controller = head }); world.Set(temp, new Temp());
Check(!VehiclePrefabMatcher.StillMatches(temp, head, matched, world), "temporary physical cannot authorize termination");
var part = world.Create(8); world.Set(part, new PrefabRef { m_Prefab = forbidden });
world.Set(head, new DynamicBuffer<LayoutElement> { new() { m_Vehicle = part } });
Check(Match(head, head, target, out matched), "canonical layout/trailer matches");
Check(VehiclePrefabMatcher.StillMatches(head, head, matched, world), "canonical layout receipt remains accepted");
var chainHead = world.Create(10); world.Set(chainHead, new PrefabRef { m_Prefab = allowed });
for (var i = 11; i <= 15; i++)
{
    var e = world.Create(i); world.Set(e, new PrefabRef { m_Prefab = forbidden });
    world.Set(e, new Controller { m_Controller = new Entity(i - 1) });
}
Check(VehiclePrefabMatcher.StillMatches(new Entity(14), chainHead, forbidden, world), "four-hop physical source remains owned");
Check(!VehiclePrefabMatcher.StillMatches(new Entity(15), chainHead, forbidden, world), "five-hop source is not assumed owned");
world.Set(new Entity(11), new Controller { m_Controller = new Entity(12) });
Check(!VehiclePrefabMatcher.StillMatches(new Entity(14), chainHead, forbidden, world), "controller cycle rejected");
var attached = world.Create(20); var attachedPart = world.Create(21);
world.Set(attached, new Controller { m_Controller = chainHead });
world.Set(attached, new PrefabRef { m_Prefab = allowed });
world.Set(attachedPart, new PrefabRef { m_Prefab = forbidden });
world.Set(attached, new DynamicBuffer<LayoutElement> { new() { m_Vehicle = attachedPart } });
Check(Match(attached, chainHead, target, out matched) && matched == forbidden, "physical layout matches through shared candidate matcher");
Check(VehiclePrefabMatcher.StillMatches(attached, chainHead, matched, world), "owned physical layout is retained by receipt");
world.Set(attached, new Controller { m_Controller = otherTarget });
Check(!VehiclePrefabMatcher.StillMatches(attached, chainHead, matched, world), "detached physical layout cannot authorize no-route result");
Console.WriteLine($"PASS: {checks} production matcher/receipt checks. Fixtures do not establish actual game archetypes or behavior.");
