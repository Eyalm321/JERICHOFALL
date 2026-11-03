#!/usr/bin/env python3
"""Run the self-contained game, capture active scenes and check loaded modules."""
from pathlib import Path
import json, os, subprocess, sys

game=Path(sys.argv[1]).resolve(); evidence=Path(sys.argv[2]).resolve()
evidence.mkdir(parents=True,exist_ok=True)
environment=os.environ.copy()
environment['SDL_AUDIODRIVER']='dummy'
environment.setdefault('LP_NUM_THREADS','2')
environment['FNA3D_FORCE_DRIVER']='D3D11' if os.name=='nt' else 'OpenGL'
if os.name!='nt':
    environment['SDL_VIDEODRIVER']='x11'
    environment['LD_LIBRARY_PATH']=str(game/'lib64')+':'+environment.get('LD_LIBRARY_PATH','')
executable=game/('Jerichofall.exe' if os.name=='nt' else 'Jerichofall')
with (evidence/'runtime.log').open('w') as log:
    result=subprocess.run([str(executable),'--save',str(evidence/'isolated-journey.json'),
        '--verify',str(evidence),'--frames','900'],cwd=game,env=environment,stdout=log,stderr=subprocess.STDOUT,timeout=120)
if result.returncode:
    print((evidence/'runtime.log').read_text());raise SystemExit(result.returncode)
report=json.loads((evidence/'render-checks.json').read_text())
required={'Title','Heroes','Map','Briefing','Campaign','Campaign-Fighting','Campaign-Paused','Versus','Versus-Fighting','Controls'}
required.update({'Campaign-Spirit','Campaign-Respawn','Versus-Spirit','Versus-Spirit-Dash','Campaign-Last-Life-Spirit'})
assert required.issubset(report['captures']), 'Missing scene captures: '+str(required-set(report['captures']))
assert 'Error' not in report['screens'], 'The game visited its error screen'
assert report['engine']=='Jerichofall.Core'
allowed = {'Jerichofall', 'Jerichofall.Core', 'FNA', 'System', 'mscorlib', 'netstandard', 'WindowsBase', 'Microsoft.CSharp'}
assert all(name in allowed or name.startswith(('System.', 'Microsoft.Win32.', 'Microsoft.VisualBasic')) for name in report['assemblies']), 'Unapproved loaded assembly' 
print(json.dumps(dict(passed=True,frames=report['frames'],captures=report['captures'],engine=report['engine'],platform=sys.platform)))
