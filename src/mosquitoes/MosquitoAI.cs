// This code was made by ratrat (https://github.com/ratrat44) and is included in Fisobs with his permission.

using RWCustom;
using System.Linq;
using UnityEngine;
using static CreatureTemplate.Relationship.Type;

namespace Mosquitoes;

// AI  使用关系追踪器
sealed class MosquitoAI : ArtificialIntelligence, IUseARelationshipTracker
{
	enum Behavior
	{
		Idle,        // 闲逛
		Swarm,       // 群聚
		Flee,        // 逃跑
		EscapeRain,  // 躲雨
		Hunt         // 狩猎
	}

	//                                  关系追踪器.被追踪生物的状态
	sealed class MosquitoTrackedState : RelationshipTracker.TrackedCreatureState
	{
		// 记录被叮咬的时间 加减法维护
		public int prickedTime;
	}

	public Mosquito bug;
	public int tiredOfHuntingCounter; // 厌倦了狩猎计数器
	public AbstractCreature? tiredOfHuntingCreature; // 厌倦了狩猎生物
	private Behavior behavior; // 当前行为
	private int behaviorCounter; //	行为计数器，用于控制行为的持续时间
	private WorldCoordinate tempIdlePos; // 临时闲逛位置，用于在闲逛行为中选择目标位置

	public MosquitoAI(AbstractCreature acrit, Mosquito bug) : base(acrit, acrit.world)
	{
		this.bug = bug;
		bug.AI = this;


		// 初始化各种追踪模块
		// 基础的 A* 寻路模块
		AddModule(new StandardPather(this, acrit.world, acrit));
		pathFinder.stepsPerFrame = 20; // 每帧步数
		// 视觉追踪器
		AddModule(new Tracker(this, 10, 10, 600, 0.5f, 5, 5, 10));
		// 看穿角落 最多精确追踪多少生物 记住生物多少帧 幽灵追踪速度 幽灵推动力 幽灵推动速度 幽灵消散距离 是否使用已追踪生物
		// 幽灵追踪 模拟目标可能的移动路径

		// 追踪威胁的模块
		AddModule(new ThreatTracker(this, 3));
		// 最多记住多少个威胁

		// 降雨追踪器
		AddModule(new RainTracker(this));
		// 巢穴查找器
		AddModule(new DenFinder(this, acrit));
		// 噪音追踪器
		AddModule(new NoiseTracker(this, tracker));
		// 猎物追踪器
		// 最多记住多少猎物 坚持偏差 确定能抓到猎物的距离 确定会失去猎物的距离 成功率估计对距离的依赖
		AddModule(new PreyTracker(this, 5, 1f, 5f, 150f, 0.05f));
		// 效用比较器
		AddModule(new UtilityComparer(this));
		// 关系追踪器
		AddModule(new RelationshipTracker(this, tracker));

		// 平滑
		var smoother = new FloatTweener.FloatTweenUpAndDown(
			// 基础浮点插值器
			// 每帧向目标值线性插值 50%，反应快。
			new FloatTweener.FloatTweenBasic(FloatTweener.TweenType.Lerp, 0.5f),
			// TweenType.Tick, 0.005f：每帧固定减少 0.005，反应慢。
			new FloatTweener.FloatTweenBasic(FloatTweener.TweenType.Tick, 0.005f));

		// 将各个模块添加到效用比较器中，并设置权重和阈值
		// 参与效用比较的 AI 模块 可选的平滑器		权重 	延续奖励
		utilityComparer.AddComparedModule(threatTracker, smoother, 1f, 1.1f);
		utilityComparer.AddComparedModule(rainTracker, null, 1f, 1.1f);
		utilityComparer.AddComparedModule(preyTracker, null, 0.4f, 1.1f);

		// 设置噪音追踪器的听觉技能
		noiseTracker.hearingSkill = 0.5f;
		behavior = Behavior.Idle;
	}

	// 当生物被发现时的处理逻辑
	//                                        第一次发现                                追踪器中的表示
	public override void CreatureSpotted(bool firstSpot, Tracker.CreatureRepresentation otherCreature)
	{
		// 如果我们在漫无目的地游荡时遇到另一名族群成员，就停下手头的事，和他们待在一起
		// If we're wandering aimlessly and we find another pack member, stop what we're doing and hang with them
		if (behavior == Behavior.Swarm && RandomPackMember() == null) {
			behaviorCounter = 0;
		}
	}

	// 用于跟踪关系的模块
	AIModule? IUseARelationshipTracker.ModuleToTrackRelationship(CreatureTemplate.Relationship relationship)
	{
		if (relationship.type == Eats) return preyTracker;
		if (relationship.type == Afraid) return threatTracker;
		return null;
	}

	// 创建受追踪的 CreatureState
	RelationshipTracker.TrackedCreatureState IUseARelationshipTracker.CreateTrackedCreatureState(RelationshipTracker.DynamicRelationship rel)
	{
		return new MosquitoTrackedState();
	}

	// 更新动态关系                                                                  关系追踪器.动态关系
	CreatureTemplate.Relationship IUseARelationshipTracker.UpdateDynamicRelationship(RelationshipTracker.DynamicRelationship dRelation)
	{
		if (dRelation.state is not MosquitoTrackedState state) return default;

		// 动态关系.跟踪器代表.视觉接触
		if (dRelation.trackerRep.VisualContact) {      //所代表的生物
			dRelation.state.alive = dRelation.trackerRep.representedCreature.state.alive;
		}

		if (!dRelation.state.alive) {
			// Relationship(Type type, float intensity)
			// 类型 强度
			return new CreatureTemplate.Relationship(Ignores, 0f);
		}

		if (dRelation.trackerRep.representedCreature.realizedObject is Creature c && c.State.alive && bug.grasps[0]?.grabbed == c) {
			// 厌倦了狩猎计数器
			state.prickedTime += 2;
			// 忘记厌倦了狩猎的生物
			// 猎物追踪器.忘记猎物(厌倦了狩猎生物)
			preyTracker.ForgetPrey(tiredOfHuntingCreature);
		} else {
			state.prickedTime -= 1;
		}

		if (state.prickedTime > 0) {
			// 害怕
			return new CreatureTemplate.Relationship(Afraid, 0.5f);
		}

		// 静态关系
		return StaticRelationship(dRelation.trackerRep.representedCreature);
	}

	public override void Update()
	{
		base.Update();

		if (bug.room == null) {
			return;
		}

		pathFinder.walkPastPointOfNoReturn = stranded
			|| denFinder.GetDenPosition() is not WorldCoordinate denPos
			|| !pathFinder.CoordinatePossibleToGetBackFrom(denPos)
			|| threatTracker.Utility() > 0.95f;

		utilityComparer.GetUtilityTracker(threatTracker).weight = Custom.LerpMap(threatTracker.ThreatOfTile(creature.pos, true), 0.1f, 2f, 0.1f, 1f, 0.5f);

		if (utilityComparer.HighestUtility() < 0.02f && (behavior != Behavior.Hunt || preyTracker.MostAttractivePrey == null))
		{
			if (behavior is not Behavior.Idle or Behavior.Swarm)
			{
				behaviorCounter = 0;
				behavior = Random.value < 0.1f ? Behavior.Idle : Behavior.Swarm;
			}
		}
		else
		{
			behavior = utilityComparer.HighestUtilityModule() switch {
				ThreatTracker => Behavior.Flee,
				RainTracker => Behavior.EscapeRain,
				PreyTracker => Behavior.Hunt,
				_ => behavior
			};
		}

		switch (behavior) {
			case Behavior.Idle:
				bug.runSpeed = Custom.LerpAndTick(bug.runSpeed, 0.6f + (0.4f * threatTracker.Utility()), 0.01f, 0.016666668f);

				WorldCoordinate coord = new(bug.room.abstractRoom.index, Random.Range(0, bug.room.TileWidth), Random.Range(0, bug.room.TileHeight), -1);
				if (IdleScore(tempIdlePos) > IdleScore(coord)) {
					tempIdlePos = coord;
				}

				if (IdleScore(tempIdlePos) < IdleScore(pathFinder.GetDestination) + Custom.LerpMap(behaviorCounter, 0f, 300f, 100f, -300f)) {
					SetDestination(tempIdlePos);
					behaviorCounter = Random.Range(100, 400);
					tempIdlePos = new WorldCoordinate(bug.room.abstractRoom.index, Random.Range(0, bug.room.TileWidth), Random.Range(0, bug.room.TileHeight), -1);
				}

				behaviorCounter--;
				break;

			case Behavior.Swarm:
				bug.runSpeed = Custom.LerpAndTick(bug.runSpeed, 0.3f + (0.7f * threatTracker.Utility()), 1f / 100f, 1f / 60f);

				if (behaviorCounter <= 0) {
					// Try to hang with another pack member, or wander if there are none
					var other = RandomPackMember();
					if (other != null) {
						var newDest = other.BestGuessForPosition();
						if (newDest.x != -1 && newDest.y != -1) {
							newDest.x += Random.Range(-10, 10);
							newDest.y += Random.Range(-10, 10);
						}
						creature.abstractAI.SetDestination(newDest);

						behaviorCounter = Random.Range(50, 100);
					} else {
						creature.abstractAI.SetDestination(creature.abstractAI.MigrationDestination);

						behaviorCounter = Random.Range(200, 400);
					}
				}

				behaviorCounter--;
				break;

			case Behavior.Flee:
				bug.runSpeed = Custom.LerpAndTick(bug.runSpeed, 1f, 0.01f, 0.1f);
				creature.abstractAI.SetDestination(threatTracker.FleeTo(creature.pos, 20, 20, true));
				break;

			case Behavior.Hunt:
				bug.runSpeed = Custom.LerpAndTick(bug.runSpeed, 1f, 0.01f, .1f);

				if (preyTracker.MostAttractivePrey != null)
					creature.abstractAI.SetDestination(preyTracker.MostAttractivePrey.BestGuessForPosition());

				tiredOfHuntingCounter++;
				if (tiredOfHuntingCounter > 100) {
					tiredOfHuntingCreature = preyTracker.MostAttractivePrey?.representedCreature;
					tiredOfHuntingCounter = 0;
					preyTracker.ForgetPrey(tiredOfHuntingCreature);
					tracker.ForgetCreature(tiredOfHuntingCreature);
				}
				break;

			case Behavior.EscapeRain:
				bug.runSpeed = Custom.LerpAndTick(bug.runSpeed, 1f, 0.01f, 0.1f);
				if (denFinder.GetDenPosition() is WorldCoordinate den) {
					creature.abstractAI.SetDestination(den);
				}
				break;
		}
	}

	// 随机选择一个族群成员
	private Tracker.CreatureRepresentation? RandomPackMember()
	{
		var others = tracker.creatures.Where(r => r.dynamicRelationship.state.alive && r.dynamicRelationship.currentRelationship.type == Pack).ToList();
		if (others.Any()) {
			return others[Random.Range(0, others.Count)];
		}
		return null;
	}

	private float IdleScore(WorldCoordinate coord)
	{
		if (coord.NodeDefined || coord.room != creature.pos.room || !pathFinder.CoordinateReachableAndGetbackable(coord) || bug.room.aimap.getAItile(coord).acc == AItile.Accessibility.Solid) {
			return float.MaxValue;
		}
		float result = 1f;
		if (bug.room.aimap.getAItile(coord).narrowSpace) {
			result += 100f;
		}
		result += threatTracker.ThreatOfTile(coord, true) * 1000f;
		result += threatTracker.ThreatOfTile(bug.room.GetWorldCoordinate((bug.room.MiddleOfTile(coord) + bug.room.MiddleOfTile(creature.pos)) / 2f), true) * 1000f;
		for (int i = 0; i < noiseTracker.sources.Count; i++) {
			result += Custom.LerpMap(Vector2.Distance(bug.room.MiddleOfTile(coord), noiseTracker.sources[i].pos), 40f, 400f, 100f, 0f);
		}
		return result;
	}

	public override bool WantToStayInDenUntilEndOfCycle()
	{
		return rainTracker.Utility() > 0.01f;
	}

	public override Tracker.CreatureRepresentation CreateTrackerRepresentationForCreature(AbstractCreature otherCreature)
	{
		return otherCreature.creatureTemplate.smallCreature
			? new Tracker.SimpleCreatureRepresentation(tracker, otherCreature, 0f, false)
			: new Tracker.ElaborateCreatureRepresentation(tracker, otherCreature, 1f, 3);
	}

	public override PathCost TravelPreference(MovementConnection coord, PathCost cost)
	{
		float val = Mathf.Max(0f, threatTracker.ThreatOfTile(coord.destinationCoord, false) - threatTracker.ThreatOfTile(creature.pos, false));
		return new PathCost(cost.resistance + Custom.LerpMap(val, 0f, 1.5f, 0f, 10000f, 5f), cost.legality);
	}
}
