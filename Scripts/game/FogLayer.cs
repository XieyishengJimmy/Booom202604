using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Booom202604;

public partial class FogLayer : Node2D
{
	public static readonly string[] CloudTexturePaths =
	[
		"res://Art/Map/Cloud1.png",
		"res://Art/Map/Cloud2.png",
		"res://Art/Map/Cloud3.png",
		"res://Art/Map/Cloud4.png",
		"res://Art/Map/Cloud5.png",
		"res://Art/Map/Cloud6.png",
	];

	static readonly Texture2D?[] CloudTextures = LoadCloudTextures();

	static readonly Texture2D? LockTexture =
		ResourceLoader.Exists("res://Art/Map/LockIcon.png") ? GD.Load<Texture2D>("res://Art/Map/LockIcon.png") : null;

	/// <summary>该地块已开始播放消散特效（用于推迟显示格上事件等）。</summary>
	public event Action<string>? FogRevealAnimationStarted;

	/// <summary>消散动画播放完毕或无需播放时触发，与 <see cref="FogRevealAnimationStarted"/> 成对。</summary>
	public event Action<string>? FogRevealAnimationFinished;

	TileMapLayer? _terrain;
	readonly Dictionary<string, Sprite2D> _sprites = [];
	readonly Dictionary<string, Sprite2D> _lockOverlays = [];

	/// <summary>相对「铺满六角格逻辑尺寸」的倍率；<c>1.0</c> 为不加额外放大。</summary>
	const float FogCoverageScaleMul = 1f;

	Texture2D[] _dissolveFrames = [];
	Vector2 _fogSpriteScale = new(0.52f, 0.52f);
	Vector2 _fogAnchorOffset = Vector2.Zero;
	Color _fogModulate = new(1f, 1f, 1f, 0.92f);

	static Texture2D?[] LoadCloudTextures()
	{
		var arr = new Texture2D?[CloudTexturePaths.Length];
		for (int i = 0; i < CloudTexturePaths.Length; i++)
		{
			string p = CloudTexturePaths[i];
			arr[i] = ResourceLoader.Exists(p) ? GD.Load<Texture2D>(p) : null;
		}

		return arr;
	}

	static Texture2D? ResolveStaticFogTexture()
	{
		if (CloudTextures[0] != null)
			return CloudTextures[0];

		if (ResourceLoader.Exists("res://Art/Map/1.png"))
			return GD.Load<Texture2D>("res://Art/Map/1.png");

		return null;
	}

	void RecomputeFogPresentation()
	{
		Texture2D? refTex = CloudTextures[0] ?? ResolveStaticFogTexture();

		if (refTex == null)
			return;

		if (_terrain?.TileSet != null)
		{
			_fogSpriteScale = ComputeFogScaleMatchTilemapDraw(_terrain.TileSet, refTex);
			_fogAnchorOffset = ComputeFogAnchorOffsetPx(_terrain.TileSet);
		}

		_dissolveFrames = CloudTextures.Where(t => t != null).Cast<Texture2D>().ToArray();
	}

	/// <summary>
	/// 与 Godot <c>TileMapLayer::draw_tile</c> 一致：目标矩形尺寸为 atlas 区域像素大小（1 纹素 ≈ 1 世界单位），
	/// 不按 <c>TileSize</c> 再乘「塞进六角」的均匀比 <c>k</c>。迷雾与地块若同分辨率则 <c>Scale (1,1)</c>。
	/// 再乘 <see cref="FogCoverageScaleMul"/>。
	/// </summary>
	static Vector2 ComputeFogScaleMatchTilemapDraw(TileSet tileSet, Texture2D fogTex)
	{
		int tw = fogTex.GetWidth();
		int th = fogTex.GetHeight();

		if (tw <= 0 || th <= 0)
			return new Vector2(0.52f * FogCoverageScaleMul, 0.52f * FogCoverageScaleMul);

		if (!TerrainTilesetFactory.TryGetPrimaryAtlasTileDrawablePixelSize(tileSet, out Vector2I atlas))
		{
			float sx = (float)tileSet.TileSize.X / tw;
			float sy = (float)tileSet.TileSize.Y / th;
			float vf = Mathf.Min(sx, sy) * FogCoverageScaleMul;

			return new Vector2(vf, vf);
		}

		float sx2 = (float)atlas.X / tw * FogCoverageScaleMul;
		float sy2 = (float)atlas.Y / th * FogCoverageScaleMul;

		return new Vector2(sx2, sy2);
	}

	static Vector2 ComputeFogAnchorOffsetPx(TileSet tileSet)
	{
		return TerrainTilesetFactory.TryGetPrimaryTileTextureOriginPx(tileSet, out Vector2I o)
			? new Vector2(o.X, o.Y)
			: Vector2.Zero;
	}

	public void Setup(TileMapLayer terrainLayer)
	{
		_terrain = terrainLayer;
		RecomputeFogPresentation();
	}

	public void Rebuild(Godot.Collections.Dictionary fogState)
	{
		ClearAll();

		foreach (Variant keyVar in fogState.Keys)
		{
			if (!fogState[keyVar].AsBool())
				continue;

			string ck = keyVar.AsString();
			Add(HexGridUtil.ParseKey(ck));
		}
	}

	public void SetCell(Vector2I cell, bool fogOn)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (fogOn)
			Add(cell);
		else
			Remove(ck);
	}

	/// <summary>在仍为「有迷雾」的格子上叠加锁图标（吸收锁定提示）。</summary>
	public void SetAbsorptionLockedVisual(Vector2I cell, bool locked)
	{
		if (_terrain == null || LockTexture == null)
			return;

		string ck = HexGridUtil.CellKey(cell);
		if (locked)
		{
			if (!_sprites.ContainsKey(ck))
				return;
			if (_lockOverlays.ContainsKey(ck))
				return;

			Vector2 lockScale = new Vector2(0.38f, 0.38f);
			if (_terrain.TileSet != null)
				lockScale = ComputeFogScaleMatchTilemapDraw(_terrain.TileSet, LockTexture);

			var s = new Sprite2D
			{
				Texture = LockTexture,
				Centered = true,
				Scale = lockScale,
				Position = FogAnchorWorld(_terrain, cell),
				ZIndex = 2,
			};
			AddChild(s);
			_lockOverlays[ck] = s;
		}
		else if (_lockOverlays.Remove(ck, out Sprite2D? lo) && lo != null)
			lo.QueueFree();
	}

	void Add(Vector2I cell)
	{
		Texture2D? tex = CloudTextures[0] ?? ResolveStaticFogTexture();
		if (_terrain == null || tex == null)
			return;

		if (_terrain.TileSet != null)
		{
			_fogSpriteScale = ComputeFogScaleMatchTilemapDraw(_terrain.TileSet, tex);
			_fogAnchorOffset = ComputeFogAnchorOffsetPx(_terrain.TileSet);
		}

		string ck = HexGridUtil.CellKey(cell);
		if (_sprites.ContainsKey(ck))
			return;

		var s = new Sprite2D
		{
			Texture = tex,
			Centered = true,
			Modulate = _fogModulate,
			Scale = _fogSpriteScale,
			Position = FogAnchorWorld(_terrain, cell),
			ZIndex = 0,
		};

		AddChild(s);
		_sprites[ck] = s;
	}

	void Remove(string ck)
	{
		if (_lockOverlays.Remove(ck, out Sprite2D? lockSpr) && lockSpr != null)
			lockSpr.QueueFree();

		if (!_sprites.Remove(ck, out Sprite2D? s) || s == null)
			return;

		Vector2 pos = s.Position;
		s.QueueFree();

		FogRevealAnimationStarted?.Invoke(ck);

		if (_dissolveFrames.Length == 0 || _terrain == null)
		{
			FogRevealAnimationFinished?.Invoke(ck);
			return;
		}

		var fx = new FogDissolveVisual();
		string capture = ck;
		fx.Finished += () => FogRevealAnimationFinished?.Invoke(capture);
		fx.Start(_dissolveFrames, _fogSpriteScale, _fogModulate);
		fx.Position = pos;
		AddChild(fx);
	}

	void ClearAll()
	{
		for (int i = GetChildCount() - 1; i >= 0; i--)
		{
			if (GetChild(i) is FogDissolveVisual d)
				d.QueueFree();
		}

		foreach (KeyValuePair<string, Sprite2D> kv in _sprites)
			kv.Value.QueueFree();

		_sprites.Clear();

		foreach (KeyValuePair<string, Sprite2D> kv in _lockOverlays)
			kv.Value.QueueFree();

		_lockOverlays.Clear();
	}

	/// <summary>与 TileMap 单格贴图可见中心对齐：<c>map_to_local - texture_origin</c>。</summary>
	Vector2 FogAnchorWorld(TileMapLayer terrain, Vector2I cell) =>
		terrain.MapToLocal(cell) - _fogAnchorOffset;
}
