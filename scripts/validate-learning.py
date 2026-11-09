"""检查纳入版本控制的学习资料；不调用模型，也不运行行为评测。"""

import json
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote, urlsplit


ROOT = Path(__file__).resolve().parents[1]


def validate() -> None:
    output = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        cwd=ROOT,
    )
    names = sorted(set(filter(None, output.decode("utf-8").split("\0"))))
    tracked_paths = {ROOT / name for name in names}
    errors: list[str] = []
    json_count = 0
    link_count = 0

    for name in names:
        path = ROOT / name
        if not path.is_file():
            errors.append(f"文件不存在：{name}")
            continue
        if path.suffix == ".json":
            try:
                json.loads(path.read_text(encoding="utf-8"))
                json_count += 1
            except (ValueError, UnicodeError) as error:
                errors.append(f"{name}: {error}")
        if path.suffix != ".md":
            continue
        for target in re.findall(r"\[[^\]]*\]\(([^)]+)\)", path.read_text(encoding="utf-8")):
            parsed = urlsplit(target)
            if parsed.scheme or parsed.netloc or not parsed.path:
                continue
            destination = (path.parent / unquote(parsed.path)).resolve()
            link_count += 1
            if destination not in tracked_paths:
                errors.append(f"{name}: 链接目标未纳入仓库：{target}")

    cases = json.loads((ROOT / "evals/starter-cases.json").read_text(encoding="utf-8"))
    case_ids = [case["id"] for case in cases["cases"]]
    if not case_ids or len(case_ids) != len(set(case_ids)):
        errors.append("评测用例 ID 为空或重复")
    for case in cases["cases"]:
        if not case.get("expected"):
            errors.append(f"{case['id']}: 缺少验收要求")
        for source in case.get("sources", []):
            if ROOT / source not in tracked_paths:
                errors.append(f"{case['id']}: 数据来源未纳入仓库：{source}")

    snapshot = json.loads((ROOT / "fixtures/service-snapshot.json").read_text(encoding="utf-8"))
    if snapshot.get("synthetic") is not True or snapshot.get("environment") != "learning":
        errors.append("服务快照必须明确标记为 learning 环境的合成数据")
    service_ids = [service["serviceId"] for service in snapshot["services"]]
    if not service_ids or len(service_ids) != len(set(service_ids)):
        errors.append("快照服务 ID 为空或重复")

    if errors:
        raise ValueError("\n".join(errors))
    print(f"PASS: {json_count} JSON 文件，{link_count} 本地文档链接，{len(case_ids)} 个唯一评测用例。")
    print("以上为静态检查，不代表模型行为评测或工具预算测试已通过。")


if __name__ == "__main__":
    try:
        validate()
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
