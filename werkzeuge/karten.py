# Pro Szene: Orte (Typ, Name, Weltposition) und Splines (Pfad, Länge, Enden)
import sys, json, math, collections, UnityPy
env = UnityPy.load(sys.argv[1])
texte = json.load(open(sys.argv[2]))
def qrot(q, v):
    q = q.get("value", q)
    x,y,z,w = q["x"],q["y"],q["z"],q["w"]; vx,vy,vz = v
    tx = 2*(y*vz - z*vy); ty = 2*(z*vx - x*vz); tz = 2*(x*vy - y*vx)
    return (vx + w*tx + (y*tz - z*ty), vy + w*ty + (z*tx - x*tz), vz + w*tz + (x*ty - y*tx))
def qmul(a,b):
    return {"w":a["w"]*b["w"]-a["x"]*b["x"]-a["y"]*b["y"]-a["z"]*b["z"],
            "x":a["w"]*b["x"]+a["x"]*b["w"]+a["y"]*b["z"]-a["z"]*b["y"],
            "y":a["w"]*b["y"]-a["x"]*b["z"]+a["y"]*b["w"]+a["z"]*b["x"],
            "z":a["w"]*b["z"]+a["x"]*b["y"]-a["y"]*b["x"]+a["z"]*b["w"]}
V = lambda d: (lambda e: (e["x"], e["y"], e["z"]))(d.get("value", d))
szenen = collections.defaultdict(lambda: {"go":{}, "tr":{}, "go_tr":{}, "mb":[], "comps":{}, "objs":{}})
infos = {}
for obj in env.objects:
    t = obj.type.name
    sz = szenen[obj.assets_file.name]
    if t in ("MeshRenderer","SkinnedMeshRenderer","ParticleSystemRenderer","MonoBehaviour","ParticleSystem","Light"):
        sz["objs"][obj.path_id] = obj
    if t == "GameObject":
        g = obj.read(); sz["go"][obj.path_id] = g.m_Name
        sz["comps"][obj.path_id] = [c.path_id for c in g.m_Components]
    elif t == "Transform":
        tt = obj.read_typetree()
        sz["tr"][obj.path_id] = tt
        sz["go_tr"][tt["m_GameObject"]["m_PathID"]] = obj.path_id
    elif t == "MonoBehaviour":
        try: tt = obj.read_typetree()
        except Exception: continue
        if "Name" in tt and "ItemId" in tt: infos[obj.path_id] = tt
        if "info" in tt or "m_Splines" in tt: sz["mb"].append(tt)
def welt(sz, tid, cache):
    if tid in cache: return cache[tid]
    tt = sz["tr"].get(tid)
    if not tt: return ((0,0,0), {"x":0,"y":0,"z":0,"w":1}, (1,1,1))
    lp, lr, ls = V(tt["m_LocalPosition"]), tt["m_LocalRotation"], V(tt["m_LocalScale"])
    f = tt["m_Father"]["m_PathID"]
    if f == 0: r = (lp, lr, ls)
    else:
        pp, pr, ps = welt(sz, f, cache)
        sp = (lp[0]*ps[0], lp[1]*ps[1], lp[2]*ps[2]); rp = qrot(pr, sp)
        r = ((pp[0]+rp[0], pp[1]+rp[1], pp[2]+rp[2]), qmul(pr, lr), (ps[0]*ls[0], ps[1]*ls[1], ps[2]*ls[2]))
    cache[tid] = r; return r
def pfad(sz, tid):
    teile=[]
    while tid:
        tt = sz["tr"].get(tid)
        if not tt: break
        teile.append(sz["go"].get(tt["m_GameObject"]["m_PathID"], "?")); tid = tt["m_Father"]["m_PathID"]
    return "/".join(reversed(teile))
def typ(mb, info):
    if "closedCrate" in mb: return "Crate"
    if info and "IsStartingCabin" in info: return "Cabin"
    if "watchScenicPointPrompts" in mb: return "Scenic"
    if info and "MeetDialogueTree" in info: return "Merchant"
    keys = set(info.keys()) if info else set()
    return "?" + ",".join(sorted(k for k in keys if k not in ("m_GameObject","m_Enabled","m_Script","m_Name","ItemId","Name","VisibilityCondition","TrackStatePerArea","ShowIconBackground","OneTimeReward","EveryTimeReward","references")))[:60]
def unterbaum(sz, tid, tiefe=0, aus=None):
    kinder = sz.setdefault("kinder", None)
    if kinder is None:
        kinder = collections.defaultdict(list)
        for k, tt in sz["tr"].items(): kinder[tt["m_Father"]["m_PathID"]].append(k)
        sz["kinder"] = kinder
    go = sz["tr"][tid]["m_GameObject"]["m_PathID"]
    teile = []
    for cid in sz["comps"].get(go, []):
        o = sz["objs"].get(cid)
        if not o: continue
        n = o.type.name
        try:
            if n == "MonoBehaviour":
                tt = o.read_typetree(); n = "MB(" + ",".join(list(tt.keys())[4:9]) + ")"
            elif "Renderer" in n:
                r = o.read(); mats = []
                for m in r.m_Materials:
                    try: mats.append(m.read().m_Name)
                    except Exception: mats.append("?")
                n += "[" + ",".join(mats) + "]"
        except Exception: pass
        teile.append(n)
    aus.append("  "*tiefe + sz["go"].get(go, "?") + "  " + " ".join(teile))
    if tiefe < 5:
        for k in sz["kinder"][tid][:20]: unterbaum(sz, k, tiefe+1, aus)
    return aus
ergebnis = {}
for name, sz in szenen.items():
    if not sz["mb"]: continue
    cache = {}; orte = []; wege = []
    for mb in sz["mb"]:
        tid = sz["go_tr"].get(mb["m_GameObject"]["m_PathID"])
        p, r, s = welt(sz, tid, cache)
        if "info" in mb:
            info = infos.get(mb["info"]["m_PathID"])
            n = None
            if info:
                kid = str(info["Name"].get("m_TableEntryReference", {}).get("m_KeyId"))
                n = texte.get(kid)
            orte.append({"typ": typ(mb, info), "name": n or (info or {}).get("m_Name") or sz["go"].get(mb["m_GameObject"]["m_PathID"]), "pos": [round(c) for c in p], "go": pfad(sz, tid)[-90:]})
        else:
            for si, sp in enumerate(mb.get("m_Splines", [])):
                ks = sp.get("m_Knots", [])
                if len(ks) < 2: continue
                def W(v): 
                    v = (v[0]*s[0], v[1]*s[1], v[2]*s[2]); v = qrot(r, v); return (p[0]+v[0], p[1]+v[1], p[2]+v[2])
                laenge = 0; pts=[]
                for i in range(len(ks)-1):
                    a, b = ks[i], ks[i+1]
                    p0 = V(a["Position"]); p3 = V(b["Position"])
                    to = qrot(a["Rotation"], V(a["TangentOut"])); ti = qrot(b["Rotation"], V(b["TangentIn"]))
                    p1 = tuple(p0[j]+to[j] for j in range(3)); p2 = tuple(p3[j]+ti[j] for j in range(3))
                    prev = None
                    for k in range(11):
                        u = k/10; m = 1-u
                        q = tuple(m*m*m*p0[j] + 3*m*m*u*p1[j] + 3*m*u*u*p2[j] + u*u*u*p3[j] for j in range(3))
                        q = W(q)
                        if prev: laenge += math.dist(prev, q)
                        prev = q
                        if k == 0 and i == 0: pts.append(q)
                    if i == len(ks)-2: pts.append(prev)
                wege.append({"pfad": pfad(sz, tid)[-110:], "i": si, "len": round(laenge), "knoten": len(ks), "a": [round(c) for c in pts[0]], "b": [round(c) for c in pts[-1]]})
    kisten = []
    for mb in sz["mb"]:
        if "closedCrate" in mb and len(kisten) < 1:
            tid = sz["go_tr"].get(mb["m_GameObject"]["m_PathID"])
            kisten.append(unterbaum(sz, tid, 0, []))
    ergebnis[name] = {"orte": orte, "wege": wege, "kiste": kisten}
json.dump(ergebnis, open(sys.argv[3], "w"), ensure_ascii=False, indent=1)
for n, e in ergebnis.items(): print(n, len(e["orte"]), "Orte", len(e["wege"]), "Splines")
