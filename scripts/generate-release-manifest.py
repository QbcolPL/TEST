#!/usr/bin/env python3
from pathlib import Path
import argparse,hashlib,json
p=argparse.ArgumentParser();p.add_argument('--root',default='.');p.add_argument('--hub');p.add_argument('--builder');p.add_argument('--out',default='fieldtak-release.json');a=p.parse_args()
r=Path(a.root);v=json.loads((r/'version.json').read_text())
def sha(path):
 h=hashlib.sha256();
 with open(path,'rb') as f:
  for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
 return h.hexdigest()
hub=Path(a.hub);builder=Path(a.builder)
out={'schema':'fieldtak.release','version':1,'channel':v['releaseChannel'],'hub':{'version':v['hubVersion'],'versionCode':v['androidVersionCode'],'asset':hub.name,'sha256':sha(hub)},'builder':{'version':v['builderVersion'],'asset':builder.name,'sha256':sha(builder)}}
Path(a.out).write_text(json.dumps(out,indent=2)+'\n')
print(Path(a.out).resolve())
