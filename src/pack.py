# -*- coding: utf-8 -*-
"""Сборка DLL и упаковка архива для Thunderstore.

Запуск:  python pack.py
Файлы мода берутся из папки MOD по явному списку PAYLOAD: нет файла — не пакуем.
Готовый архив кладётся в папку проекта, рядом с src.
После упаковки те же файлы раскладываются в INSTALL — папку профиля
Thunderstore, чтобы проверить сборку в игре. Источник правды — MOD в
репозитории: папку профиля Thunderstore переписывает при обновлениях.
"""
import io
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile

SRC = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(SRC)
MOD = os.path.join(PROJECT, "mod")
TESTS = os.path.join(PROJECT, "tests")


def profile():
    """Имя профиля Thunderstore берём из src/Local.props — он в git не попадает."""
    path = os.path.join(SRC, "Local.props")
    if os.path.isfile(path):
        found = re.search(r"<RepoProfile>(.+?)</RepoProfile>",
                          io.open(path, encoding="utf-8-sig").read())
        if found:
            return found.group(1).strip()
    return "Default"


INSTALL = os.path.join(
    os.environ.get("APPDATA", ""), "Thunderstore Mod Manager", "DataFolder",
    "REPO", "profiles", profile(), "BepInEx", "plugins",
    "Sweet_team-I_Am_Item")

PAYLOAD = [
    "manifest.json",
    "icon.png",
    "README.md",
    "CHANGELOG.md",
    "LICENSE",
    "IamItem.dll",
]


def read(path):
    return io.open(path, encoding="utf-8-sig").read()


def versions():
    """Версия живёт в трёх местах, разъедутся — сайт покажет одно, игра другое."""
    manifest = json.loads(read(os.path.join(MOD, "manifest.json")))["version_number"]
    csproj = re.search(r"<Version>(.+?)</Version>",
                       read(os.path.join(SRC, "IamItem.csproj"))).group(1)
    plugin = re.search(r'public const string Version = "(.+?)"',
                       read(os.path.join(SRC, "Plugin.cs"))).group(1)
    return {"manifest.json": manifest, "csproj": csproj, "Plugin.cs": plugin}


def main():
    found = versions()
    if len(set(found.values())) != 1:
        sys.exit("Версии разошлись, не пакую: "
                 + ", ".join("%s=%s" % kv for kv in found.items()))
    version = found["manifest.json"]

    # Проверка чисел баланса. Разъехались — дальше идти незачем.
    subprocess.check_call(["dotnet", "run", "-c", "Release"], cwd=TESTS, shell=True)

    subprocess.check_call(["dotnet", "build", "-c", "Release"], cwd=SRC, shell=True)
    shutil.copy(os.path.join(SRC, "bin", "Release", "IamItem.dll"),
                os.path.join(MOD, "IamItem.dll"))

    # README и CHANGELOG — один экземпляр в корне, чтобы страница на гитхабе и
    # страница на Thunderstore не разъезжались.
    for name in ("README.md", "CHANGELOG.md"):
        shutil.copy(os.path.join(PROJECT, name), os.path.join(MOD, name))

    missing = [f for f in PAYLOAD if not os.path.isfile(os.path.join(MOD, f))]
    if missing:
        sys.exit("В папке мода нет файлов, не пакую: " + ", ".join(missing))

    out = os.path.join(PROJECT, "I_Am_Item-%s.zip" % version)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for f in PAYLOAD:
            z.write(os.path.join(MOD, f), f)

    print("Готово: %s (%d файлов, версия %s)" % (out, len(PAYLOAD), version))

    # ponytail: раскладка в профиль — простое копирование поверх, без удаления
    # лишнего. Понадобится чистая установка — снести папку профиля руками.
    if os.path.isdir(INSTALL):
        for f in PAYLOAD:
            shutil.copy(os.path.join(MOD, f), os.path.join(INSTALL, f))
        print("Разложено для проверки в игре: %s" % INSTALL)
    else:
        print("Папки профиля нет, в игру не разложил: %s" % INSTALL)


if __name__ == "__main__":
    main()
