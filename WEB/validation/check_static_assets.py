#!/usr/bin/env python3
"""Package-only regression checks; does NOT run MSBuild or ASP.NET Core."""
from pathlib import Path
import hashlib, json
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / 'server/src/NaverSabermetrics.Web'
ASSETS = ('index.html', 'app.css', 'home.css', 'player-profile.css', 'app.js', 'games.js', 'diamond.js', 'analysis.js', 'analysis.css', 'comparison.js', 'comparison.css')


def validate(root=ROOT):
    web=root/'server/src/NaverSabermetrics.Web'
    failures=[]
    project=ET.parse(web/'NaverSabermetrics.Web.csproj')
    for name in ASSETS:
        src=root/'frontend'/name
        dst=web/'wwwroot'/name
        if not src.is_file() or not dst.is_file():
            failures.append('missing physical source/webroot asset: '+name)
        elif not dst.stat().st_size or src.read_bytes()!=dst.read_bytes():
            failures.append('empty or mismatched asset: '+name)
    for item in project.findall('.//Content'):
        if item.get('Link','').startswith('wwwroot/'):
            failures.append('virtual wwwroot link still registered')
    target=project.find("Target[@Name='SyncFrontendAssets']")
    if target is None or 'AssignTargetPaths' not in target.get('BeforeTargets',''):
        failures.append('missing pre-discovery frontend synchronization')
    else:
        for task in ('Error','MakeDir','Copy'):
            if target.find(task) is None: failures.append('sync target missing '+task)
    items=set()
    for item in project.findall('.//Content'):
        if item.get('Include','').startswith('wwwroot/'):
            items.update(item.get('Include').split(';'))
            if item.get('CopyToOutputDirectory')!='PreserveNewest' or item.get('CopyToPublishDirectory')!='PreserveNewest':
                failures.append('public files not configured for both build and publish')
    if items!={'wwwroot/'+name for name in ASSETS}: failures.append('public content allowlist mismatch')
    for entry in ('verify-web.ps1','start-local.ps1'):
        if 'check-static-assets.ps1' not in (root/'scripts'/entry).read_text(encoding='utf-8-sig'):
            failures.append('no runtime-manifest preflight in '+entry)
    program=(web/'Program.cs').read_text(encoding='utf-8-sig')
    if program.find('foreach (var asset') > program.find('WebApplication.CreateBuilder'):
        failures.append('asset guard must precede host creation')
    for private in ('*.db','appsettings*.json','*.cs','*.dll','*.ps1'):
        if list((web/'wwwroot').rglob(private)):
            failures.append('non-public file under wwwroot: '+private)
    return failures

if __name__=='__main__':
    failures=validate()
    print(json.dumps({'status':'FAIL' if failures else 'STATIC_ASSET_PACKAGE_CHECK_PASS',
        'checks':'physical files, copy metadata, preflight wiring, public file allowlist',
        'dotnet_build':'NOT_RUN','windows_execution':'NOT_RUN','issues':failures},indent=2))
    raise SystemExit(bool(failures))
