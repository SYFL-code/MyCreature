using DevInterface;
using Fisobs.Creatures;
using Fisobs.Properties;
using Fisobs.Sandbox;
using RWCustom;
using System.Collections.Generic;
using UnityEngine;
// 路径成本.合法性
using static PathCost.Legality;
// 生物模板.类型
using CreatureType = CreatureTemplate.Type;

namespace Mosquitoes;

sealed class MosquitoCritob : Critob
{
	public static readonly CreatureType Mosquito = new("Mosquito", true);
	// 多人游戏解锁.沙盒解锁ID
	public static readonly MultiplayerUnlocks.SandboxUnlockID MosquitoUnlock = new("Mosquito", true);

	public MosquitoCritob() : base(Mosquito)
	{
		LoadedPerformanceCost = 20f;
		SandboxPerformanceCost = new(linear: 0.6f, exponential: 0.1f);
		ShelterDanger = ShelterDanger.Safe;
		CreatureName = "Blood Sucker";

		RegisterUnlock(killScore: KillScore.Configurable(2), MosquitoUnlock, parent: MultiplayerUnlocks.SandboxUnlockID.Slugcat, data: 0);
	}

	public override CreatureTemplate CreateTemplate()
	{
		// 在创建新的 CreatureTemplate 时，CreatureFormula 会为你处理大部分繁琐的工作，
		// 但如有需要，你也可以手动构建一个 CreatureTemplate。
		// CreatureFormula does most of the ugly work for you when creating a new CreatureTemplate,
		// but you can construct a CreatureTemplate manually if you need to.

		/*
		public enum Legality
		{
			Allowed,            // 0：允许
			Unwanted,           // 1：不想要，但可以走
			IllegalConnection,  // 2：非法连接
			IllegalTile,        // 3：非法地块
			SolidTile,          // 4：固体地块
			Unallowed           // 5：完全不允许
		}
		 */

		CreatureTemplate t = new CreatureFormula(this)
		{
			DefaultRelationship = new(CreatureTemplate.Relationship.Type.Eats, 0.25f),
			HasAI = true,
			InstantDeathDamage = 1,
			Pathing = PreBakedPathing.Ancestral(CreatureType.Fly),

			TileResistances = new()
			{
				// 空气   阻力 允许
				Air = new(1, Allowed),
			},
			ConnectionResistances = new()
			{
				// 标准连接
				Standard = new(1, Allowed),
				// 对角线
				OpenDiagonal = new(1, Allowed),
				// 快捷方式
				ShortCut = new(1, Allowed),
				// NPC 运输
				NPCTransportation = new(10, Allowed),
				// 屏幕外移动
				OffScreenMovement = new(1, Allowed),
				// 跨房间
				BetweenRooms = new(1, Allowed),
			},

			DamageResistances = new()
			{
				Base = 0.95f,
			},
			StunResistances = new()
			{
				Base = 0.6f,
			}
		}.IntoTemplate();

		// 以下属性源自原版生物，因此你最好手头有反编译源代码的副本。
		// The below properties are derived from vanilla creatures, so you should have your copy of the decompiled source code handy.

		// 关于 CreatureTemplate 字段的一些说明：
		// Some notes on the fields of CreatureTemplate:

		// offScreenSpeed       how fast the creature moves between abstract rooms
		// abstractLaziness     how long it takes the creature to start migrating
		// smallCreature        determines if rocks instakill, if large predators ignore it, etc
		// dangerToPlayer       DLLs are 0.85, spiders are 0.1, pole plants are 0.5
		// waterVision          0..1 how well the creature can see through water
		// throughSurfaceVision 0..1 how well the creature can see through water surfaces
		// movementBasedVision  0..1 bonus to vision for moving creatures
		// lungCapacity         ticks until the creature falls unconscious from drowning
		// quickDeath           determines if the creature should die as determined by Creature.Violence(). if false, you must define custom death logic
		// saveCreature         determines if the creature is saved after a cycle ends. false for overseers and garbage worms
		// hibernateOffScreen   true for deer, miros birds, leviathans, vultures, and scavengers
		// bodySize             batflies are 0.1, eggbugs are 0.4, DLLs are 5.5, slugcats are 1

		// offScreenSpeed       生物在抽象房间之间移动的速度
		// abstractedLaziness     生物开始迁徙需要多长时间
		// smallCreature        决定石头是否能即死、大型掠食者是否忽略它等
		// dangerToPlayer       对玩家的危险程度：DLL 是 0.85，蜘蛛是 0.1，杆状植物是 0.5
		// waterVision          0..1 生物透过水看清的程度
		// throughSurfaceVision 0..1 生物透过水面看清的程度
		// movementBasedVision  0..1 对移动中生物的视觉加成
		// lungCapacity         生物溺水昏迷前的 tick 数
		// quickDeath           决定生物是否按 Creature.Violence() 的方式死亡。如果为 false，你必须定义自定义死亡逻辑
		// saveCreature         决定生物是否在循环结束后被保存。对于监视者和垃圾虫为 false
		// hibernateOffScreen   对于鹿、Miros 鸟、利维坦、秃鹫和拾荒者为 true
		// bodySize             蝠蝇是 0.1，蛋虫是 0.4，DLL 是 5.5，蛞蝓猫是 1


		t.offScreenSpeed = 0.1f; // 生物在抽象房间之间移动的速度
		t.abstractedLaziness = 200; // 生物开始迁徙需要多长时间
		t.roamBetweenRoomsChance = 0.07f; // 在房间之间漫游的概率
		t.bodySize = 0.5f; // 体型大小
		t.stowFoodInDen = true; // 将食物收进巢穴
		t.shortcutSegments = 2; // 快捷方式段数
		t.grasps = 1; // 最大抓握数
		t.visualRadius = 800f; // 视觉半径
		t.movementBasedVision = 0.65f; // 移动视觉加成
		t.communityInfluence = 0.1f; // 社区影响力
		t.waterRelationship = CreatureTemplate.WaterRelationship.AirAndSurface; // 陆生 海生 空生
		t.waterPathingResistance = 2f; // 水中路径阻力
		t.canFly = true; // 能否飞行
		t.meatPoints = 3; // 肉点数
		t.dangerousToPlayer = 0.4f; // 对玩家的危险程度

		return t;
	}

	public override void EstablishRelationships()
	{
		// You can use StaticWorld.EstablishRelationship, but the Relationships class exists to make this process more ergonomic.
		// 你可以使用 StaticWorld.EstablishRelationship，但 Relationships 类的存在正是为了让这个过程更加便捷。

		Relationships self = new(Mosquito);

		// 静态世界.生物模板.所有模板
		foreach (var template in StaticWorld.creatureTemplates)
		{
			// quantified 量化
			if (template.quantified)
			{
				// 忽略
				self.Ignores(template.type);
				// 被忽略
				self.IgnoredBy(template.type);
			}
		}

		// 是否包含在内
		self.IsInPack(Mosquito, 1f);

		// 吃
		self.Eats(CreatureType.Slugcat, 0.4f);
		self.Eats(CreatureType.Scavenger, 0.6f);
		self.Eats(CreatureType.LizardTemplate, 0.3f);
		self.Eats(CreatureType.CicadaA, 0.4f); // 蝉乌贼
											   // 蝉 A，蝉 B（分别是白色和黑色的鱿鱼）

		// 恐吓
		self.Intimidates(CreatureType.LizardTemplate, 0.35f);
		self.Intimidates(CreatureType.CicadaA, 0.3f);

		// 攻击
		self.AttackedBy(CreatureType.Slugcat, 0.2f);
		self.AttackedBy(CreatureType.Scavenger, 0.2f);

		// 被吃
		self.EatenBy(CreatureType.BigSpider, 0.35f);

		// 恐惧
		self.Fears(CreatureType.Spider, 0.2f);
		self.Fears(CreatureType.BigSpider, 0.2f);
		self.Fears(CreatureType.SpitterSpider, 0.6f);
	}

	public override ArtificialIntelligence CreateRealizedAI(AbstractCreature acrit)
	{
		return new MosquitoAI(acrit, (Mosquito)acrit.realizedCreature);
	}

	public override Creature CreateRealizedCreature(AbstractCreature acrit)
	{
		return new Mosquito(acrit);
	}

														// 运动连接 
	public override void ConnectionIsAllowed(AImap map, MovementConnection connection, ref bool? allowed)
	{
		// allowed
		//null：不干预，使用游戏默认判断。
		//true：强制允许。
		//false：强制禁止。
		// 如果 allowed 原本是 null（不干预）：
		//表达式为 true → 结果还是 null，继续不干预。
		//表达式为 false → 结果变成 false，强制禁止。

		// DLL 不会穿过起点和终点在同一房间内的捷径——它们只穿过房间出口。
		// 要模拟这种行为，可以使用类似下面的代码：
		// DLLs don't travel through shortcuts that start and end in the same room—they only travel through room exits.
		// To emulate this behavior, use something like:

		// 一般捷径
		//ShortcutData.Type n = ShortcutData.Type.Normal;
		//if (connection.type == MovementConnection.MovementType.ShortCut)
		//{
		//	// & 是逻辑与
		//	// && 的优先级高于 ||
		//	// TileDefined 具体地块
		//	// startCoord 起始坐标
		//	// destinationCoord 目的地坐标
		//	allowed &=
		//		(connection.startCoord.TileDefined && map.room.shortcutData(connection.StartTile).shortCutType == n) ||
		//		(connection.destinationCoord.TileDefined && map.room.shortcutData(connection.DestTile).shortCutType == n)
		//		;
		//}
		//// BigCreatureShortCutSqueeze（大型生物挤过捷径）
		//else if (connection.type == MovementConnection.MovementType.BigCreatureShortCutSqueeze)
		//{
		//	// ShortcutEntrance 快捷入口
		//	allowed &=
		//		(map.room.GetTile(connection.startCoord).Terrain == Room.Tile.TerrainType.ShortcutEntrance && map.room.shortcutData(connection.StartTile).shortCutType == n) ||
		//		(map.room.GetTile(connection.destinationCoord).Terrain == Room.Tile.TerrainType.ShortcutEntrance && map.room.shortcutData(connection.DestTile).shortCutType == n)
		//		;
		//}
	}

	public override void TileIsAllowed(AImap map, IntVector2 tilePos, ref bool? allowed)
	{
		// 像秃鹫、Miros 鸟和 DLL 这样的大型生物需要 2 格自由空间才能移动。利维坦需要 4 格！它们都无法通过单格隧道。
		// 要模拟这种行为，可以使用类似下面的代码：
		// Large creatures like vultures, miros birds, and DLLs need 2 tiles of free space to move around in. Leviathans need 4! None of them can fit in one-tile tunnels.
		// To emulate this behavior, use something like:

		//allowed &= map.IsFreeSpace(tilePos, tilesOfFreeSpace: 2);

		// DLL 虽然胖，但能挤进捷径。
		// 要模拟这种行为，可以使用类似下面的代码：
		// DLLs can fit into shortcuts despite being fat.
		// To emulate this behavior, use something like:

		// ShortcutEntrance 快捷入口
		//allowed |= map.room.GetTile(tilePos).Terrain == Room.Tile.TerrainType.ShortcutEntrance;
	}

	public override IEnumerable<string> WorldFileAliases()
	{
		yield return "mosq";
		yield return "bloodsucker";
	}

	public override IEnumerable<RoomAttractivenessPanel.Category> DevtoolsRoomAttraction()
	{
		yield return RoomAttractivenessPanel.Category.Flying;
		yield return RoomAttractivenessPanel.Category.LikesWater;
		yield return RoomAttractivenessPanel.Category.LikesOutside;
	}

	public override string DevtoolsMapName(AbstractCreature acrit)
	{
		return "mqto";
	}

	public override Color DevtoolsMapColor(AbstractCreature acrit)
	{
		// Default would return the mosquito's icon color (which is gray), which is fine, but red is better.
		// 默认情况下会返回蚊子的图标颜色（即灰色），这样也可以，但红色更好。
		return new Color(.7f, .4f, .4f);
	}

	public override ItemProperties? Properties(Creature crit)
	{
		// If you don't need the `forObject` parameter, store one ItemProperties instance as a static object and return that.
		// The CentiShields example demonstrates this.
		// 如果不需要 `forObject` 参数，请将一个 ItemProperties 实例作为静态对象存储，并返回该实例。
		// CentiShields 示例对此进行了演示。
		if (crit is Mosquito mosquito)
		{
			return new MosquitoProperties(mosquito);
		}

		return null;
	}
}
