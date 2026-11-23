import sys,gzip,numpy as np
sys.path.insert(0,sys.argv[1])
import os; os.chdir(sys.argv[1])
from stift import kreis
d=b''.join(np.asarray(kreis(3,f))[::-1,:,3].tobytes() for f in range(3))  # Unity: Zeile 0 = unten
open(sys.argv[2],'wb').write(gzip.compress(d,9,mtime=0)); print(len(d))
