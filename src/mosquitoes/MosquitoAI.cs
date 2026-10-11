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

	// 当发现新生物的处理逻辑
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

		// 走过无法回头点
		pathFinder.walkPastPointOfNoReturn =
			// 受困
			stranded
			// 找不到巢穴位置
			|| denFinder.GetDenPosition() is not WorldCoordinate denPos
			// 返回巢穴的路径是否还可行
			|| !pathFinder.CoordinatePossibleToGetBackFrom(denPos)
			// 当前威胁效用大于 0.95
			|| threatTracker.Utility() > 0.95f;

		// 设置威胁追踪器的效用权重，基于当前生物位置的威胁程度
		// 在构造函数中，威胁追踪器的权重被设为 1f                                                                   威胁生物实际能到达
		utilityComparer.GetUtilityTracker(threatTracker).weight = Custom.LerpMap(threatTracker.ThreatOfTile(creature.pos, true), 0.1f, 2f, 0.1f, 1f, 0.5f);

		// 所有模块的效用都非常低 && (不在狩猎 || 没有最吸引人的猎物)
		if (utilityComparer.HighestUtility() < 0.02f && (behavior != Behavior.Hunt || preyTracker.MostAttractivePrey == null))
		{
			if (behavior is not (Behavior.Idle or Behavior.Swarm))
			{
				behaviorCounter = 0;
				behavior = Random.value < 0.1f ? Behavior.Idle : Behavior.Swarm;
			}
		}
		else
		{
			behavior =
				utilityComparer.HighestUtilityModule() switch
				{
					ThreatTracker => Behavior.Flee,
					RainTracker => Behavior.EscapeRain,
					PreyTracker => Behavior.Hunt,
					_ => behavior
				};
		}


		switch (behavior)
		{
			case Behavior.Idle:
				// 调整速度
				// 0.6 + (0.4 * 威胁追踪器的效用)
				bug.runSpeed = Custom.LerpAndTick(bug.runSpeed, 0.6f + (0.4f * threatTracker.Utility()), 0.01f, 1f / 60f);

				// 采样候选点
				// -1：表示不是节点，只是一个普通地块。
				WorldCoordinate coord = new(bug.room.abstractRoom.index, Random.Range(0, bug.room.TileWidth), Random.Range(0, bug.room.TileHeight), -1);
				if (IdleScore(tempIdlePos) > IdleScore(coord)) {
					tempIdlePos = coord;
				}

				// 决定是否换目的地
				// 如果临时闲逛位置的分数小于当前目标位置的分数 + 行为计数器映射到的值，则设置新的目标位置
				if (IdleScore(tempIdlePos) < IdleScore(pathFinder.GetDestination) + Custom.LerpMap(behaviorCounter, 0f, 300f, 100f, -300f)) {
					SetDestination(tempIdlePos);
					behaviorCounter = Random.Range(100, 400);
					tempIdlePos = new WorldCoordinate(bug.room.abstractRoom.index, Random.Range(0, bug.room.TileWidth), Random.Range(0, bug.room.TileHeight), -1);
				}

				// 递减计时器
				behaviorCounter--;
				break;

			case Behavior.Swarm:
				// 调整速度
				bug.runSpeed = Custom.LerpAndTick(bug.runSpeed, 0.3f + (0.7f * threatTracker.Utility()), 0.01f, 1f / 60f);

				if (behaviorCounter <= 0) {
					// Try to hang with another pack member, or wander if there are none
					// 尽量和另一名成员待在一起，如果没有的话就四处游荡
					var other = RandomPackMember();
					if (other != null) {
						// 最佳位置猜测
						var newDest = other.BestGuessForPosition();
						if (newDest.x != -1 && newDest.y != -1) {
							newDest.x += Random.Range(-10, 10);
							newDest.y += Random.Range(-10, 10);
						}
						// 设置目的地
						creature.abstractAI.SetDestination(newDest);

						behaviorCounter = Random.Range(50, 100);
					} else {
						// 迁徙目的地
						creature.abstractAI.SetDestination(creature.abstractAI.MigrationDestination);

						behaviorCounter = Random.Range(200, 400);
					}
				}

				behaviorCounter--;
				break;

			case Behavior.Flee:
				bug.runSpeed = Custom.LerpAndTick(bug.runSpeed, 1f, 0.01f, 0.1f);//  评估20次  搜索半径20格  允许逃出当前房间  考虑回到巢穴
				creature.abstractAI.SetDestination(threatTracker.FleeTo(creature.pos, 20, 20, true, false));
				break;

			case Behavior.Hunt:
				bug.runSpeed = Custom.LerpAndTick(bug.runSpeed, 1f, 0.01f, 0.1f);

				// 如果有最吸引人的猎物，就设置目的地为该猎物的最佳位置猜测
				if (preyTracker.MostAttractivePrey != null)
					creature.abstractAI.SetDestination(preyTracker.MostAttractivePrey.BestGuessForPosition());

				// 如果厌倦了狩猎计数器超过100，就忘记最吸引人的猎物
				tiredOfHuntingCounter++;
				if (tiredOfHuntingCounter > 100) {
					tiredOfHuntingCreature = preyTracker.MostAttractivePrey?.representedCreature;
					tiredOfHuntingCounter = 0;
					// 从猎物追踪器中移除该猎物。
					preyTracker.ForgetPrey(tiredOfHuntingCreature);
					// 从视觉追踪器中移除该生物。
					tracker.ForgetCreature(tiredOfHuntingCreature);
				}
				break;

			case Behavior.EscapeRain:
				bug.runSpeed = Custom.LerpAndTick(bug.runSpeed, 1f, 0.01f, 0.1f);

				// 如果找到了巢穴位置，就设置目的地为巢穴位置
				if (denFinder.GetDenPosition() is WorldCoordinate den) {
					creature.abstractAI.SetDestination(den);
				}
				break;
		}
	}

	// 随机选择一个族群成员
	private Tracker.CreatureRepresentation? RandomPackMember()
	{
		// Pack 表示同族/同群
		var others = tracker.creatures.Where(r => r.dynamicRelationship.state.alive && r.dynamicRelationship.currentRelationship.type == Pack).ToList();
		if (others.Any()) {
			return others[Random.Range(0, others.Count)];
		}
		return null;
	}

	// 计算闲逛位置的分数，分数越低表示位置越适合闲逛
	private float IdleScore(WorldCoordinate coord)
	{
		if (// 定义了抽象节点
			coord.NodeDefined ||
			// 不在同一个房间
			coord.room != creature.pos.room ||
			// 寻路器无法到达
			// 坐标的可达性与可回溯性
			!pathFinder.CoordinateReachableAndGetbackable(coord) ||
			// 地块是固体
			bug.room.aimap.getAItile(coord).acc == AItile.Accessibility.Solid)
		{
			return float.MaxValue;
		}

		float result = 1f;
		// 狭窄空间
		if (bug.room.aimap.getAItile(coord).narrowSpace) {
			result += 100f;
		}
		// 候选点本身的威胁
		result += threatTracker.ThreatOfTile(coord, true) * 1000f;
		// 候选点与生物当前位置的中点的威胁
		result += threatTracker.ThreatOfTile(bug.room.GetWorldCoordinate((bug.room.MiddleOfTile(coord) + bug.room.MiddleOfTile(creature.pos)) / 2f), true) * 1000f;

		for (int i = 0; i < noiseTracker.sources.Count; i++) {
			// 距离 ≤ 40 → 加 100 分。
			// 距离 ≥ 400 → 加 0 分。
			result += Custom.LerpMap(Vector2.Distance(bug.room.MiddleOfTile(coord), noiseTracker.sources[i].pos), 40f, 400f, 100f, 0f);
		}
		return result;
	}

	// 决定是否在巢穴中停留直到周期结束
	public override bool WantToStayInDenUntilEndOfCycle()
	{
		return rainTracker.Utility() > 0.01f;
	}

	// 创建追踪器表示
	public override Tracker.CreatureRepresentation CreateTrackerRepresentationForCreature(AbstractCreature otherCreature)
	{
		return otherCreature.creatureTemplate.smallCreature
			? new Tracker.SimpleCreatureRepresentation(tracker, otherCreature, 0f, false)// 优先级为0, 不可见时不忘记
			: new Tracker.ElaborateCreatureRepresentation(tracker, otherCreature, 1f, 3);// 优先级为1, 最多 3 个幽灵
	}

	// 计算移动偏好，基于威胁程度的变化
	public override PathCost TravelPreference(MovementConnection coord, PathCost cost)
	{
		// 计算威胁值的变化
		float val = Mathf.Max(0f, threatTracker.ThreatOfTile(coord.destinationCoord, false) - threatTracker.ThreatOfTile(creature.pos, false));
		return new PathCost(cost.resistance + Custom.LerpMap(val, 0f, 1.5f, 0f, 10000f, 5f), cost.legality);
	}
}
