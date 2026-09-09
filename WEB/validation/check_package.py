#!/usr/bin/env python3
"""Offline structural checks. NOT a C# build, HTTP integration or browser test."""
from pathlib import Path
import hashlib, json, re, shutil, sqlite3, subprocess, sys, zipfile
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
import validate_relational_warehouse as warehouse
from static_source_check import strip_comments_and_literals
from check_static_assets import validate as validate_static_assets
ROOT=Path(__file__).resolve().parents[1]
issues=[]; metrics={}
projects=list((ROOT/'server/src').glob('*/*.csproj'))
metrics['projects']=len(projects)
for path in projects:
    xml=ET.parse(path)
    for el in xml.findall('.//ProjectReference'):
        target=path.parent/el.attrib['Include'].replace('\\','/')
        if not target.exists():issues.append('missing project '+str(target))
    if 'WindowsForms' in path.read_text(): issues.append('Web solution must not require WinForms')
csfiles=list((ROOT/'server').rglob('*.cs'));metrics['csharp_files']=len(csfiles)
for path in csfiles:
    masked=strip_comments_and_literals(path.read_text(encoding='utf-8-sig'))
    pairs={')':'(',']':'[','}':'{'};stack=[]
    for c in masked:
        if c in '([{': stack.append(c)
        elif c in pairs:
            if not stack or stack.pop()!=pairs[c]:issues.append('delimiter mismatch '+path.name);break
    if stack:issues.append('unclosed delimiter '+path.name)
warehouse.ROOT=ROOT;warehouse.SRC=ROOT/'server/src/NaverRelay.Infrastructure.Sqlite'
con=sqlite3.connect(':memory:');con.executescript(warehouse.extract_schema());columns=warehouse.table_columns(con)
metrics['schema_tables']=len(columns)
metrics['schema_indexes']=con.execute("SELECT count(*) FROM sqlite_master WHERE type='index' AND sql IS NOT NULL").fetchone()[0]
metrics['insert_shapes'],errors=warehouse.validate_insert_shapes(columns);issues+=errors
metrics['sql_templates_explained'],metrics['sql_templates_not_reconstructed'],errors=warehouse.validate_sql(con);issues+=errors
metrics['sample_source_structure']=warehouse.validate_sample();issues+=metrics['sample_source_structure']['issues']
for table,cols in columns.items():
    if set(cols)&{'NormalizedJson','RawJson','SourceJson'}: issues.append('full-source JSON column '+table)
manifest=json.loads((ROOT/'docs/source-baseline-manifest.json').read_text())
metrics['baseline_files']=len(manifest['files']);metrics['modified_baseline_files']=[]
for entry in manifest['files']:
    path=ROOT/entry['path']; actual=hashlib.sha256(path.read_bytes()).hexdigest()
    if actual!=entry['packagedSha256']:issues.append('manifest checksum '+entry['path'])
    if entry['modified']:metrics['modified_baseline_files'].append(entry['path'])
for name in ['KboPitcherWarMath.cs','DatabaseCacheService.PitcherWarCalibration.cs']:
    entry=next(f for f in manifest['files'] if f['path'].endswith('/'+name))
    if entry['modified']:issues.append('core formula changed '+name)
metrics['core_war_formula_files_byte_identical']=not any('core formula' in e for e in issues)
views=(ROOT/'server/src/NaverSabermetrics.Web/ViewRegistry.cs').read_text()
for role in ['batter','pitcher','constants']:
    metrics['views_'+role]=len(re.findall(r'new\("'+role+'",',views))
allcsharp='\n'.join(p.read_text() for p in csfiles)
for typ in re.findall(r'new\("\w+", "[^"]+", "[^"]+", typeof\((\w+)\)\)',views):
    if not re.search(r'\b(class|record)\s+'+typ+r'\b',allcsharp): issues.append('unknown view DTO '+typ)
html=(ROOT/'frontend/index.html').read_text(); js=(ROOT/'frontend/app.js').read_text()
class IdParser(HTMLParser):
    def __init__(self):super().__init__();self.ids=[]
    def handle_starttag(self,tag,attrs):
        for k,v in attrs:
            if k=='id':self.ids.append(v)
p=IdParser();p.feed(html)
if len(p.ids)!=len(set(p.ids)):issues.append('duplicate HTML id')
for ident in set(re.findall(r"\$\('([^']+)'\)",js)):
    if ident not in p.ids:issues.append('missing DOM id '+ident)
node=shutil.which('node')
metrics['javascript_syntax']='NOT_RUN: node missing'
if node:
    checked=subprocess.run([node,'--check',str(ROOT/'frontend/app.js')],text=True,capture_output=True)
    metrics['javascript_syntax']='PASS' if checked.returncode==0 else 'FAIL'
    if checked.returncode:issues.append(checked.stderr)
metrics['dotnet_build']='NOT_RUN: requires .NET 8 SDK and NuGet restore'
metrics['actual_http_tests']='NOT_RUN: run verify-web.bat on Windows'
metrics['browser_layout']='NOT_RUN: open the actual running app in Chrome/Edge'
metrics['actual_user_db']='NOT_RUN: user database not supplied'
asset_issues=validate_static_assets(ROOT)
metrics['static_asset_package']='FAIL' if asset_issues else 'PASS'
issues+=asset_issues
metrics['issues']=issues
metrics['status']='OFFLINE_STRUCTURAL_CHECKS_PASS' if not issues else 'FAIL'
result=json.dumps(metrics,ensure_ascii=False,indent=2)
print(result)
if '--write' in sys.argv:(ROOT/'docs/OFFLINE_VALIDATION.json').write_text(result+'\n',encoding='utf-8')
sys.exit(bool(issues))
