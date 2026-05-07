# excel_to_json.py
# 使用方法: python excel_to_json.py <excel文件路径> <输出文件夹路径>

import os
import sys
import json
from pathlib import Path

try:
    import pandas as pd
except ImportError:
    print("请先安装 pandas: pip install pandas")
    print("还需要安装 openpyxl: pip install openpyxl")
    sys.exit(1)


def excel_to_json(excel_path: str, output_folder: str):
    """
    将 Excel 文件的第一个工作表转换为 JSON
    
    Args:
        excel_path: Excel 文件路径
        output_folder: 输出文件夹路径
    """
    # 检查输入文件是否存在
    if not os.path.exists(excel_path):
        print(f"错误: 找不到文件 {excel_path}")
        return False
    
    # 创建输出文件夹
    Path(output_folder).mkdir(parents=True, exist_ok=True)
    
    # 读取 Excel 的第一个工作表
    df = pd.read_excel(excel_path, sheet_name=0)
    
    # 处理 NaN 值
    df = df.fillna("")
    
    # 转换为字典列表
    data = df.to_dict(orient="records")
    
    # 生成输出文件名（用 Excel 文件名，扩展名改为 .json）
    file_name = os.path.splitext(os.path.basename(excel_path))[0]
    output_path = os.path.join(output_folder, f"{file_name}.json")
    
    # 保存为 JSON
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    
    print(f"已导出: {output_path}")
    print(f"  记录数: {len(data)}")
    print(f"  列名: {list(df.columns)}")
    
    return True


def process_folder(input_folder: str, output_folder: str):
    """
    处理文件夹内所有 Excel 文件
    
    Args:
        input_folder: 输入文件夹路径
        output_folder: 输出文件夹路径
    """
    import glob
    
    # 查找所有 Excel 文件
    excel_files = glob.glob(os.path.join(input_folder, "*.xlsx"))
    excel_files.extend(glob.glob(os.path.join(input_folder, "*.xls")))
    
    if not excel_files:
        print(f"在 {input_folder} 中未找到 Excel 文件")
        return
    
    print(f"找到 {len(excel_files)} 个 Excel 文件")
    print("=" * 50)
    
    success_count = 0
    for excel_path in excel_files:
        print(f"\n处理: {os.path.basename(excel_path)}")
        if excel_to_json(excel_path, output_folder):
            success_count += 1
    
    print("\n" + "=" * 50)
    print(f"完成! 成功: {success_count}/{len(excel_files)}")


def main():
    # 用法1: 命令行参数
    if len(sys.argv) >= 3:
        input_path = sys.argv[1]
        output_path = sys.argv[2]
        
        if os.path.isfile(input_path):
            # 单个文件
            excel_to_json(input_path, output_path)
        elif os.path.isdir(input_path):
            # 文件夹
            process_folder(input_path, output_path)
        else:
            print(f"错误: 路径不存在 {input_path}")
    else:
        # 用法2: 直接修改这里的路径
        print("=" * 50)
        print("Excel 转 JSON 工具（仅 Sheet1）")
        print("=" * 50)
        print("用法1: python excel_to_json.py <输入路径> <输出文件夹>")
        print("用法2: 直接修改脚本底部的变量\n")
        
        # ===== 在这里配置你的路径 =====
        INPUT_FOLDER = r"C:\your_excel_folder"      # 改成你的 Excel 文件夹路径
        OUTPUT_FOLDER = r"C:\output_json_folder"    # 改成你的输出文件夹路径
        # =============================
        
        print(f"使用配置:")
        print(f"  输入: {INPUT_FOLDER}")
        print(f"  输出: {OUTPUT_FOLDER}")
        
        if os.path.exists(INPUT_FOLDER):
            process_folder(INPUT_FOLDER, OUTPUT_FOLDER)
        else:
            print(f"\n请先修改脚本中的 INPUT_FOLDER 和 OUTPUT_FOLDER 路径")
            print(f"当前 INPUT_FOLDER 不存在: {INPUT_FOLDER}")


if __name__ == "__main__":
    main()