# Englische Texte: KeyId -> Text (aus StringTables)
import sys, UnityPy, json
env = UnityPy.load(sys.argv[1])
texte = {}
for obj in env.objects:
    if obj.type.name != "MonoBehaviour": continue
    try: tt = obj.read_typetree()
    except Exception: continue
    for e in tt.get("m_TableData", []):
        if "m_Id" in e and "m_Localized" in e: texte[str(e["m_Id"])] = e["m_Localized"]
json.dump(texte, open(sys.argv[2], "w"), ensure_ascii=False)
print(len(texte), "Texte")
