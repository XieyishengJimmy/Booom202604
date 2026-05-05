# 策划表（Excel `.xlsx`）

工作簿放在仓库根目录的 **`excel/`** 下，本目录仅存 **说明文档**与 Godot **翻译占位**（`.translation`），表格目录更干净。

表格文件清单：

| 文件 | 用途 |
|------|------|
| `excel/monsters.xlsx` | 怪物表 |
| `excel/bosses.xlsx` | BOSS 表 |

每个工作簿使用 **第一个工作表**；**第 1 行** 为列标题（须与下方一致），从 **第 2 行** 起为数据。

### `monsters.xlsx` 列（按首行文字）

1. **ID**（**整数**，全局唯一，`> 0`）  
2. **怪物名**  
3. **怪物描述**  
4. **怪物属性**（填「力量」或「魔力」等，规则同旧 CSV）  
5. **怪物战斗力**（整数）  
6. **怪物图片路径**（如 `res://Art/Icon/monster.png`）

### `bosses.xlsx` 列

1. **ID**（**整数**，全局唯一，`> 0`）  
2. **BOSS名**  
3. **蓄力行动条**（整数）  
4. **预警行动条**（整数）  
5. **每回合增长**（整数）  
6. **BOSS技能定位（使用位置）** — 策划表内可带换行枚举说明；首行须含此短标题。填 **整数**（如 1=中心锁定玩家格、2=范围含玩家格、3=全图任意），空或非整数导出为 `0`。  
7. **BOSS技能范围定义（生效范围）** — 同上，整数枚举（如 1=半径 1 圆、2=半径 2 圆等），导出 `skill_area`。  
8. **技能具体效果** — 若整格为 **纯数字** `1～99`，导出为枚举字段 **`skill_effect`**；否则整段文字导出为 **`skill_detail`**，`skill_effect` 为 `0`（可与列 6～7 的枚举组合描述技能）。  

导出的 **`Data/bosses.json`** 中每条 BOSS 另含：**`skill_target`**、**`skill_area`**、**`skill_effect`**、**`skill_detail`**（无文案时为空串）。兼容旧逻辑的 **`skill_description`**、**`ai_description`** 仍会被填充（分别来自自由文案或枚举摘要、以及定位列生成的简短 AI 提示）。顶层 **`version`** 为 **3**。  

若表头格子内写有长说明（多行文本），导出工具会为 **第一行标题** 建立别名；列校验以短标题关键字为准。

### 关卡 JSON 中的引用

- 事件：`monster_id` 为 **int**，指向 `monsters.json` 的 `id`。  
- 关卡根：`boss.boss_id` 为 **int**，指向 `bosses.json` 的 `id`。  
- 旧关卡里若仍是字符串 ID，加载时会尝试 `int.Parse` 兼容一次；新数据请全部用数字。

## 导出到游戏（JSON）

在项目根目录 `Booom202604` 下执行（需已安装 .NET 8）：

```bash
# 一键导出所有「已注册」的表（推荐；与根目录 export_all_tables.bat 等价）
dotnet run --project Tools/MonsterCsvToJson/MonsterCsvToJson.csproj -- export-all

# Windows：双击或命令行运行（先 cd 到 Booom202604）
# export_all_tables.bat
```

当前已注册：`monsters` → `Data/monsters.json`，`bosses` → `Data/bosses.json`。在 `excel/` 里放了新的 `foo.xlsx` 但尚未在代码里注册时，运行 `export-all` 会**提示**但不会中断已注册表的导出。

```bash
# 同时导出怪物 + BOSS（默认；会先补全缺失的示例 xlsx 模板再导出）
dotnet run --project Tools/MonsterCsvToJson/MonsterCsvToJson.csproj -- all

# 仅怪物
dotnet run --project Tools/MonsterCsvToJson/MonsterCsvToJson.csproj -- monsters

# 仅 BOSS
dotnet run --project Tools/MonsterCsvToJson/MonsterCsvToJson.csproj -- bosses

# 将 excel 下缺失的 xlsx 写成带数字 ID 的示例表（不覆盖已有文件）
dotnet run --project Tools/MonsterCsvToJson/MonsterCsvToJson.csproj -- templates

# 强制覆盖 excel 内示例表为数字 ID 模板（慎用：会覆盖现有 xlsx）
dotnet run --project Tools/MonsterCsvToJson/MonsterCsvToJson.csproj -- templates --force
```

输出：

- `Data/monsters.json` — 运行时 `MonsterTable`、关卡编辑器怪物列表  
- `Data/bosses.json` — `BossTable`、关卡编辑器 BOSS 下拉  

## 从旧 CSV 迁移为 xlsx（一次性）

若你本地还留着以前的 `monsters.csv` / `bosses.csv`（UTF-8）：

```bash
dotnet run --project Tools/MonsterCsvToJson/MonsterCsvToJson.csproj -- migrate
```

会在 `excel/` 下生成对应的 `.xlsx`（首列 ID 需你自行改为整数）。

## 关卡编辑器里如何「实时」看到新表

1. 保存 Excel 中的 `.xlsx`。  
2. 再执行上面的 `dotnet run ... -- all`（或 `monsters` / `bosses`）。  
3. **保持关卡编辑器打开**：工程会约每 **1.25 秒** 检测 `Data/monsters.json` / `Data/bosses.json` 的修改时间；发现更新后会 **自动 Reload 并刷新怪物列表 / BOSS 下拉**，无需重启 Godot。  

## 翻译文件

原与 xlsx 同目录的 `.translation` 已移至 **`docs/excel/translations/`**，避免与表格混在一起；若编辑器仍引用旧路径，请在 Godot 中重新绑定或忽略未使用的占位。
