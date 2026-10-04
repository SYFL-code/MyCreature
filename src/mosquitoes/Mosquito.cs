// This code was made by ratrat (https://github.com/ratrat44) and is included in Fisobs with his permission.
// 此代码由 ratrat（https://github.com/ratrat44）编写，经其许可收录于 Fisobs 中。

using RWCustom;
using System.Linq;
using UnityEngine;

namespace Mosquitoes;

// 会呼吸的生物 肺部/氧气系统
// 昆虫状生物 中毒系统
// 玩家可食用
sealed class Mosquito : InsectoidCreature, IPlayerEdible
{
	// 模式
	enum Mode
	{
		Free,
		StuckInChunk
	}

	public MosquitoAI AI = null!;
	public float runSpeed;
	public Vector2 needleDir; // 针头方向
	public Vector2 lastNeedleDir;
	public float bloat; // 0-1 饱和度
	public float lastBloat;

	// IntVector2 stuckTile;

	int explodeCounter; // 爆炸计数器
	int stuckCounter; // 卡住计数器
	MovementConnection? lastFollowedConnection; // 上次跟随的连接
	Vector2 travelDir; // 移动方向
	Vector2 stuckPos; // 卡住位置
	Vector2 stuckDir; // 卡住方向
	Mode mode; // 当前模式

	public Mosquito(AbstractCreature acrit) : base(acrit, acrit.world)
	{
		bodyChunks = new BodyChunk[1];
		bodyChunks[0] = new BodyChunk(this, 0, new Vector2(0f, 0f), 8f, .15f);
		bodyChunkConnections = new BodyChunkConnection[0]; // 联系

		needleDir = Custom.RNV();
		lastNeedleDir = needleDir;

		airFriction = 0.98f; // 空气摩擦
		gravity = 0.9f; // 重力
		bounce = 0.1f; // 弹力
		surfaceFriction = 0.4f; // 表面摩擦
		collisionLayer = 1; // 碰撞层
		waterFriction = 0.9f; // 水摩擦
		buoyancy = 0.94f; // 浮力
	}

	public override Color ShortCutColor()
	{
		return new Color(.7f, .4f, .4f);
	}

	public override void InitiateGraphicsModule()
	{
		graphicsModule ??= new MosquitoGraphics(this);
		graphicsModule.Reset(); // 重置
	}

	public override void Update(bool eu)
	{
		base.Update(eu);

		if (room == null) {
			return;
		}

		lastNeedleDir = needleDir;

		if (grasps[0] == null && mode == Mode.StuckInChunk) {
			ChangeMode(Mode.Free);
		}

		switch (mode) {
			case Mode.Free:
				needleDir += travelDir * .1f; // 移动方向影响针头方向
				needleDir.y = -Mathf.Abs(needleDir.y) - .1f; // 针头向下
				needleDir.Normalize(); // 归一化
				break;
			case Mode.StuckInChunk:
				BodyChunk stuckInChunk = grasps[0].grabbedChunk;

				needleDir = Custom.RotateAroundOrigo(stuckDir, Custom.VecToDeg(stuckInChunk.Rotation));
				firstChunk.pos = StuckInChunkPos(stuckInChunk) + Custom.RotateAroundOrigo(stuckPos, Custom.VecToDeg(stuckInChunk.Rotation));
				firstChunk.vel *= 0f;

				if (stuckCounter > 0) {
					stuckCounter -= Consious ? 1 : 3;
				} else {
					ChangeMode(Mode.Free);
					break;
				}

				if (Consious && grasps[0].grabbed is Creature c && !c.dead) {
					lastBloat = bloat;
					bloat = Mathf.Min(bloat + .003f, 1f);
				}

				if (bloat >= 1f && explodeCounter == 0) {
					explodeCounter += 20;
				}
				break;
		}

		if (explodeCounter > 0) {
			explodeCounter--;

			if (explodeCounter == 0) {
				Explode();
			}
		}

		if (Consious) {
			Act();
		} else {
			// 如果被抓住，允许穿过地板
			GoThroughFloors = grabbedBy.Any();
		}
	}

	void Explode()
	{
		room.AddObject(new Explosion(room, this, firstChunk.pos, 7, 150f, 4.2f, 1.5f, 200f, 0.25f, this, 0.7f, 160f, 1f));
		room.AddObject(new Explosion.ExplosionLight(firstChunk.pos, 180f, 1f, 7, Color.red));
		room.AddObject(new Explosion.ExplosionLight(firstChunk.pos, 130f, 1f, 3, new Color(1f, 1f, 1f)));
		room.AddObject(new ExplosionSpikes(room, firstChunk.pos, 14, 30f, 9f, 7f, 170f, Color.red));
		room.AddObject(new ShockWave(firstChunk.pos, 230f, 0.045f, 5));
		room.PlaySound(SoundID.Bomb_Explode, firstChunk.pos, 0.7f, 1.1f);
		LoseAllGrasps();
		Destroy();
	}

	void Act()
	{
		AI.Update();

		Vector2 followingPos = bodyChunks[0].pos; // 跟随位置
		
		// 如果已到达目的地且威胁低，停止移动并返回
												  //     路径查找器.获取目的地            威胁追踪器.实用程序()  0-1
		if ((room.GetWorldCoordinate(followingPos) == AI.pathFinder.GetDestination) && AI.threatTracker.Utility() < 0.5f)
		{
			GoThroughFloors = false;
			return;
		}

		// 获取标准寻路器
		//              路径查找器 标准路径
		var pather = AI.pathFinder as StandardPather;
		var movementConnection = pather!.FollowPath(room.GetWorldCoordinate(followingPos), true);// true:实际上正在沿着这条路径前进

		// 如果没拿到有效连接，再试一次
		if (movementConnection == default)
		{
			movementConnection = pather.FollowPath(room.GetWorldCoordinate(followingPos), true);
		}


		// 如果有移动连接，执行 Run
		if (movementConnection != default)
		{
			Run(movementConnection);
		}
		// 否则尝试朝上一个连接的目标移动，并处理水中挣扎
		else
		{
			if (lastFollowedConnection != null)
			{
				MoveTowards(room.MiddleOfTile(lastFollowedConnection.Value.DestTile)); // DestTile 目标瓷砖
			}
			// 浸没
			if (Submersion > 0.5)
			{
				// 挣扎、扑腾
				firstChunk.vel += new Vector2((Random.value - 0.5f) * 0.5f, Random.value * 0.5f);
				if (Random.value < 0.1f) {
					bodyChunks[0].vel += new Vector2((Random.value - 0.5f) * 2f, Random.value * 1.5f);
				}
			}
			GoThroughFloors = false;
		}
	}

	// 移动到指定位置
	void MoveTowards(Vector2 moveTo)
	{
		Vector2 dir = Custom.DirVec(firstChunk.pos, moveTo);
		travelDir = dir;
		bodyChunks[0].vel.y += Mathf.Lerp(gravity, gravity - buoyancy, bodyChunks[0].submersion);
		firstChunk.pos += dir;
		firstChunk.vel += dir * 2f;
		firstChunk.vel *= 0.85f;

		GoThroughFloors = moveTo.y < bodyChunks[0].pos.y - 5f;
	}

	// 执行移动连接
	void Run(MovementConnection followingConnection)
	{
		// 如果是捷径或 NPC 运输，设置进入捷径的起始位置
		if (followingConnection.type is MovementConnection.MovementType.ShortCut or MovementConnection.MovementType.NPCTransportation)
		{
			enteringShortCut = new IntVector2?(followingConnection.StartTile);

			if (followingConnection.type == MovementConnection.MovementType.NPCTransportation)
			{
				// NPC运输目的地                                   目的地坐标
				NPCTransportationDestination = followingConnection.destinationCoord;
			}
		}
		// 普通移动
		else
		{
			//                                                目标瓷砖
			MoveTowards(room.MiddleOfTile(followingConnection.DestTile));
		}
		// 记录最后跟随的连接
		lastFollowedConnection = followingConnection;
	}

	Vector2 StuckInChunkPos(BodyChunk chunk)
	{
		return chunk.owner?.graphicsModule is PlayerGraphics g ? g.drawPositions[chunk.index, 0] : chunk.pos;
	}

	// 碰撞处理
	public override void Collide(PhysicalObject otherObject, int myChunk, int otherChunk)
	{
		base.Collide(otherObject, myChunk, otherChunk);

		if (Consious && grasps[0] == null && otherObject is Creature c && c.State.alive &&
			// 猎物追踪器.最具吸引力的猎物?.所代表的生物 == c.abstractCreature)
			AI.preyTracker.MostAttractivePrey?.representedCreature == c.abstractCreature)
		{
			StickIntoChunk(otherObject, otherChunk);
		}
	}

	void StickIntoChunk(PhysicalObject otherObject, int otherChunk)
	{
		stuckCounter =
			otherObject switch
		{
			Creature { dead: false } => Random.Range(75, 150),
			Creature => Random.Range(50, 100),
			_ => Random.Range(25, 50),
		};

		BodyChunk chunk = otherObject.bodyChunks[otherChunk];

		firstChunk.pos = chunk.pos + (Custom.DirVec(chunk.pos, firstChunk.pos) * chunk.rad) + (Custom.DirVec(chunk.pos, firstChunk.pos) * 11f);
		stuckPos = Custom.RotateAroundOrigo(firstChunk.pos - StuckInChunkPos(chunk), -Custom.VecToDeg(chunk.Rotation));
		stuckDir = Custom.RotateAroundOrigo(Custom.DirVec(firstChunk.pos, Custom.DirVec(firstChunk.pos, chunk.pos)), -Custom.VecToDeg(chunk.Rotation));

		// Grab(PhysicalObject obj, int graspUsed, int chunkGrabbed,
		//  Grasp.Shareability shareability, float dominance, bool overrideEquallyDominant, bool pacifying)
		// 可分享性 优势地位 覆盖“同等主导” 安抚
		Grab(otherObject, 0, otherChunk, Grasp.Shareability.CanOnlyShareWithNonExclusive, 0.5f, false, false);

		if (grasps[0]?.grabbed is Creature grabbed)
		{
			grabbed.Violence(firstChunk, Custom.DirVec(firstChunk.pos, chunk.pos) * 3f, chunk, null, DamageType.Stab, 0.06f, 7f);
		}
		else
		{
			chunk.vel += Custom.DirVec(firstChunk.pos, chunk.pos) * 3f / chunk.mass;
		}

		new DartMaggot.DartMaggotStick(abstractPhysicalObject, chunk.owner.abstractPhysicalObject);

		ChangeMode(Mode.StuckInChunk);
	}

	void ChangeMode(Mode newMode)
	{
		if (mode != newMode)
		{
			mode = newMode;
			CollideWithTerrain = mode == Mode.Free;

			if (mode == Mode.Free)
			{
				abstractPhysicalObject.LoseAllStuckObjects();
				LoseAllGrasps();
				Stun(20);
				room.PlaySound(SoundID.Spear_Dislodged_From_Creature, firstChunk, false, 0.8f, 1.2f);
			}
			else
			{
				room.PlaySound(SoundID.Dart_Maggot_Stick_In_Creature, firstChunk, false, 0.8f, 1.2f);
			}
		}
	}

	public override void Violence(BodyChunk source, Vector2? directionAndMomentum, BodyChunk hitChunk, Appendage.Pos hitAppendage, DamageType type, float damage, float stunBonus)
	{
		base.Violence(source, directionAndMomentum, hitChunk, hitAppendage, type, damage, stunBonus);

		if (source?.owner is Weapon && directionAndMomentum.HasValue) {
			hitChunk.vel = source.vel * source.mass / hitChunk.mass;
		}

		float speed = Mathf.Max(1, directionAndMomentum.GetValueOrDefault().magnitude);

		if (bloat > 0.75f && Random.value < speed * (bloat - 0.65f)) {
			explodeCounter += 20;

			Debug.Log("exploded from violence");
		}
	}

	int bites = 2; // 剩余咬数

    public int BitesLeft => bites; // 剩余咬数
    public int FoodPoints => (int)Mathf.Lerp(1f, 3f, bloat); // 食物点数
    bool IPlayerEdible.Edible => true; // 可食用
    bool IPlayerEdible.AutomaticPickUp => false; // 自动拾取

    void IPlayerEdible.ThrowByPlayer() { }

	void IPlayerEdible.BitByPlayer(Grasp grasp, bool eu)
	{
		if (bloat > 0.75f)
		{
			explodeCounter += 20;
		}
		else
		{
			bites--;
		}

		room.PlaySound(bites == 0 ? SoundID.Slugcat_Final_Bite_Fly : SoundID.Slugcat_Bite_Fly, firstChunk.pos);

		firstChunk.MoveFromOutsideMyUpdate(eu, grasp.grabber.mainBodyChunk.pos);

		if (bites == 0 && grasp.grabber is Player p)
		{
			p.ObjectEaten(this);
			grasp.Release();
			Destroy();
		}
	}
}
