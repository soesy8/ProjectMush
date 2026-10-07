// Import authored UI only. Gameplay objects, scripts, scene GUIDs and build settings stay local.
const fs = require('node:fs');
const path = require('node:path');
const target = path.resolve(__dirname, '..');
const reference = 'E:/MergeProject';
const read = (base, file) => fs.readFileSync(path.join(base, file), 'utf8').replace(/\r\n/g, '\n');
function parse(text) {
    return new Map([...text.matchAll(/^--- !u!(\d+) &(-?\d+)(?: stripped)?\n[\s\S]*?(?=^--- !u!|$(?![\s\S]))/gm)]
        .map(match => [match[2], { id: match[2], type: Number(match[1]), text: match[0] }]));
}
const field = (block, key) => block?.text.match(new RegExp('^  ' + key + ': (.*)$', 'm'))?.[1];
const ref = (block, key) => field(block, key)?.match(/fileID: (-?\d+)/)?.[1];
const name = block => {
    const value = field(block, 'm_Name');
    try { return value?.startsWith('"') ? JSON.parse(value) : value; } catch { return value; }
};
const script = block => block?.text.match(/m_Script: \{fileID: \d+, guid: (\w+)/)?.[1] || '';
const kind = block => block.type + ':' + script(block);
const indexes = new WeakMap();
function index(blocks) {
    if (indexes.has(blocks)) return indexes.get(blocks);
    const result = { transforms: new Map(), kinds: new Map(), paths: new Map() };
    for (const b of blocks.values()) {
        if ((b.type === 4 || b.type === 224) && ref(b, 'm_GameObject')) result.transforms.set(ref(b, 'm_GameObject'), b);
        const key = kind(b);
        if (!result.kinds.has(key)) result.kinds.set(key, []);
        result.kinds.get(key).push(b);
    }
    indexes.set(blocks, result);
    return result;
}
const transform = (blocks, go) => index(blocks).transforms.get(go);
function objectPath(blocks, id) {
    if (index(blocks).paths.has(id)) return index(blocks).paths.get(id);
    const block = blocks.get(id);
    if (!block) return '';
    const t = block.type === 1 ? transform(blocks, id) : (block.type === 4 || block.type === 224) ? block : transform(blocks, ref(block, 'm_GameObject'));
    const parts = [];
    const seen = new Set();
    let current = t;
    while (current && !seen.has(current.id)) {
        seen.add(current.id);
        parts.unshift(name(blocks.get(ref(current, 'm_GameObject'))) || 'stripped:' + current.id);
        current = blocks.get(ref(current, 'm_Father'));
    }
    const value = parts.join('/');
    index(blocks).paths.set(id, value);
    return value;
}
function subtree(blocks, root) {
    const ids = new Set([root]);
    let changed = true;
    while (changed) {
        changed = false;
        for (const b of blocks.values()) {
            if (ids.has(b.id)) continue;
            if ((b.type === 4 || b.type === 224) && ids.has(ref(b, 'm_Father')) ||
                b.type === 1001 && ids.has(b.text.match(/m_TransformParent: \{fileID: (-?\d+)/)?.[1]) ||
                ids.has(ref(b, 'm_GameObject')) || ids.has(ref(b, 'm_PrefabInstance'))) {
                ids.add(b.id); changed = true;
            }
        }
        for (const id of [...ids]) {
            const b = blocks.get(id);
            for (const linked of [ref(b, 'm_GameObject'), ref(b, 'm_PrefabInstance')]) {
                if (linked && linked !== '0' && blocks.has(linked) && !ids.has(linked)) {
                    ids.add(linked); changed = true;
                }
            }
        }
    }
    return ids;
}
function findRoot(blocks, label) {
    const go = [...blocks.values()].find(b => b.type === 1 && name(b) === label);
    return go && transform(blocks, go.id)?.id;
}
function matchBlock(source, destination, id) {
    if (id === '0') return '0';
    const b = source.get(id);
    if (!b) return null;
    const same = destination.get(id);
    if (same && kind(same) === kind(b) && (objectPath(source, id) === objectPath(destination, id) || !ref(b, 'm_GameObject') && b.type !== 1)) return id;
    const candidates = index(destination).kinds.get(kind(b)) || [];
    const fullPath = objectPath(source, id);
    const exact = candidates.filter(c => objectPath(destination, c.id) === fullPath);
    if (exact.length === 1) return exact[0].id;
    const leaf = fullPath.split('/').at(-1);
    const byName = candidates.filter(c => objectPath(destination, c.id).split('/').at(-1) === leaf);
    if (byName.length === 1) return byName[0].id;
    if (b.type === 114 && script(b) && candidates.length === 1) return candidates[0].id;
    const correspondence = field(b, 'm_CorrespondingSourceObject');
    const prefab = candidates.filter(c => correspondence && correspondence.includes('guid:') && field(c, 'm_CorrespondingSourceObject') === correspondence);
    return prefab.length === 1 ? prefab[0].id : null;
}
function planScene(file) {
    const original = read(target, file), source = parse(read(reference, file)), destination = parse(original);
    const labels = file.endsWith('/Title.unity') ? ['Canvas'] : file.endsWith('/PM_Lobby.unity') ?
        ['Lobby Dog Status UI', 'Lobby Pause UI', 'MAP BOARD Panel', 'MONEY BAG SHOP Panel', 'HOUSE FLOOR PLAN Panel', 'Lobby Station Travel Menu',
            ...[...source.values()].filter(b => b.type === 1 && name(b)?.endsWith(' Hover Panel')).map(name)] :
        ['Mush Scene UI', 'TrackUI', ...findRoot(source, 'Delivery Result Panel') ? ['Delivery Result Panel'] : []];
    const groups = labels.map(label => ({ label, src: findRoot(source, label), dst: findRoot(destination, label) })).filter(g => g.src);
    const sourceIds = new Set(groups.flatMap(g => [...subtree(source, g.src)]));
    // Unity can embed procedural sprites, textures and meshes directly in a scene.
    for (const id of sourceIds) {
        for (const match of source.get(id).text.matchAll(/\{fileID: (-?\d+)\}/g)) {
            const resource = source.get(match[1]);
            if (resource && [21, 28, 43, 213].includes(resource.type)) sourceIds.add(resource.id);
        }
    }
    const removed = new Set(groups.filter(g => g.dst).flatMap(g => [...subtree(destination, g.dst)]));
    const kept = new Map([...destination].filter(([id]) => !removed.has(id)));
    let next = 98000000000000n;
    const incoming = new Map([...sourceIds].map(id => [id, String(++next)]));
    const oldToNew = new Map();
    const unresolved = [];
    for (const g of groups.filter(g => g.dst)) {
        const srcSubset = new Map([...subtree(source, g.src)].map(id => [id, source.get(id)]));
        const dstSubset = new Map([...subtree(destination, g.dst)].map(id => [id, destination.get(id)]));
        oldToNew.set(g.dst, incoming.get(g.src));
        oldToNew.set(ref(destination.get(g.dst), 'm_GameObject'), incoming.get(ref(source.get(g.src), 'm_GameObject')));
        for (const [id] of dstSubset) {
            if (oldToNew.has(id)) continue;
            const mapped = matchBlock(destination, srcSubset, id);
            if (mapped) oldToNew.set(id, incoming.get(mapped));
        }
    }
    // The art lobby uses a different XR rig instance. Keep the local gameplay camera.
    const controllerGuid = read(target, 'Assets/Mush/Lobby/Runtime/MushLobbyController.cs.meta').match(/guid: (\w+)/)[1];
    const srcController = [...source.values()].find(b => script(b) === controllerGuid);
    const dstController = [...kept.values()].find(b => script(b) === controllerGuid);
    const external = new Map();
    if (srcController && dstController) {
        external.set(srcController.id, dstController.id);
        const srcCamera = ref(srcController, 'lobbyCamera'), dstCamera = ref(dstController, 'lobbyCamera');
        if (srcCamera && dstCamera) {
            external.set(srcCamera, dstCamera);
            const srcTransform = transform(source, ref(source.get(srcCamera), 'm_GameObject'));
            const dstTransform = transform(destination, ref(destination.get(dstCamera), 'm_GameObject'));
            if (srcTransform && dstTransform) external.set(srcTransform.id, dstTransform.id);
        }
    }
    const resolve = id => incoming.get(id) || external.get(id) || matchBlock(source, kept, id);
    const localRefs = /\{fileID: (-?\d+)\}/g;
    const remapSource = text => text.replace(localRefs, (all, id) => {
        if (id === '0') return all;
        const mapped = resolve(id);
        if (!mapped) { unresolved.push({ source: id, object: objectPath(source, id), kind: source.get(id) && kind(source.get(id)) }); return all; }
        return '{fileID: ' + mapped + '}';
    });
    const imported = [...sourceIds].map(id => {
        const b = source.get(id);
        let text = remapSource(b.text).replace(/^--- !u!(\d+) &-?\d+/, '--- !u!$1 &' + incoming.get(id));
        const group = groups.find(g => g.src === id);
        if (group?.dst) {
            const parent = ref(destination.get(group.dst), 'm_Father');
            text = text.replace(/^  m_Father: .*$/m, '  m_Father: {fileID: ' + parent + '}');
        }
        return { ...b, id: incoming.get(id), text };
    });
    const output = new Map([...kept].map(([id, b]) => [id, { ...b, text: b.text.replace(localRefs, (all, old) => {
        if (!removed.has(old)) return all;
        const mapped = oldToNew.get(old);
        if (!mapped) { unresolved.push({ removed: old, owner: objectPath(destination, id), kind: kind(b) }); return all; }
        return '{fileID: ' + mapped + '}';
    }) }]));
    for (const b of imported) output.set(b.id, b);
    for (const g of groups.filter(g => !g.dst)) {
        const parent = resolve(ref(source.get(g.src), 'm_Father'));
        const newRoot = incoming.get(g.src);
        const parentBlock = output.get(parent);
        if (parentBlock) {
            parentBlock.text = parentBlock.text.replace('  m_Children: []', '  m_Children:')
                .replace(/(  m_Children:\n(?:  - \{fileID: -?\d+\}\n)*)/, '$1  - {fileID: ' + newRoot + '}\n');
        } else if (parent === '0') {
            const roots = [...output.values()].find(b => b.type === 1660057539);
            roots.text += '  - {fileID: ' + newRoot + '}\n';
        }
    }
    // Add only authored UI references to existing gameplay owners.
    for (const b of output.values()) {
        if (b.type !== 114 || !script(b)) continue;
        const srcId = [...source.values()].find(s => !sourceIds.has(s.id) && script(s) && script(s) === script(b) && matchBlock(source, kept, s.id) === b.id)?.id;
        const srcBlock = source.get(srcId);
        if (!srcBlock) continue;
        const uiFields = script(b) === controllerGuid ? ['mapPanel', 'shopPanel', 'housingPanel', 'mapStatusText', 'shopStatusText', 'housingStatusText'] :
            /MushMapRideBootstrap/.test(field(b, 'm_EditorClassIdentifier') || '') ? ['resultPanel'] :
            /MushLobbyStationNavigator/.test(field(b, 'm_EditorClassIdentifier') || '') ? ['menuRoot', 'buttonMaterial', 'selectedMaterial'] :
            /MushLobbyInteractable/.test(field(b, 'm_EditorClassIdentifier') || '') ? ['hoverPanelCanvas', 'buttonArtwork'] : [];
        for (const key of uiFields) {
            const value = field(srcBlock, key);
            if (!value) continue;
            // Keep result panels already authored locally for stages that have none in the reference.
            if (key === 'resultPanel' && value === '{fileID: 0}') continue;
            const line = '  ' + key + ': ' + remapSource(value);
            const re = new RegExp('^  ' + key + ': .*$', 'm');
            b.text = re.test(b.text) ? b.text.replace(re, () => line) : b.text + line + '\n';
        }
    }
    return { file, groups: groups.map(g => g.label), removed: removed.size, imported: imported.length, unresolved,
        text: original.slice(0, original.indexOf('--- !u!')) + [...output.values()].map(b => b.text).join('') };
}
const files = ['Title', 'PM_Lobby', 'Track_v2', 'Tree', 'SharpCurve'].map(n => 'Assets/Scenes/' + n + '.unity');
function walk(directory, prefix = '') {
    return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
        const relative = prefix + entry.name;
        return entry.isDirectory() ? walk(path.join(directory, entry.name), relative + '/') : [relative];
    });
}
function assetIndex(base) {
    const result = new Map();
    for (const file of walk(path.join(base, 'Assets')).filter(f => f.endsWith('.meta'))) {
        const guid = read(base, 'Assets/' + file).match(/^guid: (\w+)/m)?.[1];
        if (guid) result.set(guid, file.slice(0, -5));
    }
    return result;
}
function planAssets(plans) {
    const src = assetIndex(reference), dst = assetIndex(target), queue = new Set(), copies = new Set();
    for (const p of plans) for (const b of parse(p.text).values()) {
        if (!b.id.startsWith('980000')) continue;
        for (const match of b.text.matchAll(/guid: ([a-f0-9]{32})/g)) queue.add(match[1]);
    }
    for (const guid of queue) {
        const file = src.get(guid);
        if (!file || file.endsWith('.cs')) continue;
        const existing = dst.get(guid);
        if (existing && !/Art\/Textures\/UI|FontStyle|Improved|UI\/Baked|UI_Panel_Sample/.test(file)) continue;
        const sourceFile = path.join(reference, 'Assets', file);
        if (existing && fs.readFileSync(sourceFile).equals(fs.readFileSync(path.join(target, 'Assets', existing)))) continue;
        if (!existing && fs.existsSync(path.join(target, 'Assets', file))) throw new Error('GUID collision: ' + file);
        copies.add(file);
        if (/\.(asset|prefab|mat|shader|cginc)$/.test(file)) {
            const content = read(reference, 'Assets/' + file);
            for (const match of content.matchAll(/guid: ([a-f0-9]{32})/g)) queue.add(match[1]);
            for (const match of content.matchAll(/#include\s+"([^"]+)"/g)) {
                const include = path.posix.join(path.posix.dirname(file), match[1]);
                if (fs.existsSync(path.join(reference, 'Assets', include))) copies.add(include);
            }
        }
    }
    // TMP source font references also use a GUID string field rather than a PPtr.
    for (const file of walk(path.join(reference, 'Assets/Art/Scenes/Test/FontStyle/Source'))) {
        if (!file.endsWith('.meta')) copies.add('Art/Scenes/Test/FontStyle/Source/' + file);
    }
    return [...copies];
}
function apply() {
    const plans = files.map(planScene);
    if (plans.some(p => p.unresolved.length)) throw new Error(JSON.stringify(plans.map(p => ({ file: p.file, unresolved: p.unresolved }))));
    const assets = planAssets(plans);
    const backup = path.join(target, 'Logs/ArtOptUiBackup');
    fs.mkdirSync(backup, { recursive: true });
    for (const file of assets) {
        const destination = path.join(target, 'Assets', file);
        fs.mkdirSync(path.dirname(destination), { recursive: true });
        fs.copyFileSync(path.join(reference, 'Assets', file), destination);
        fs.copyFileSync(path.join(reference, 'Assets', file + '.meta'), destination + '.meta');
        let directory = path.posix.dirname(file);
        while (directory !== '.') {
            const meta = path.join(target, 'Assets', directory + '.meta');
            if (!fs.existsSync(meta) && fs.existsSync(path.join(reference, 'Assets', directory + '.meta')))
                fs.copyFileSync(path.join(reference, 'Assets', directory + '.meta'), meta);
            directory = path.posix.dirname(directory);
        }
    }
    for (const p of plans) {
        const saved = path.join(backup, path.basename(p.file));
        if (!fs.existsSync(saved)) fs.copyFileSync(path.join(target, p.file), saved);
        fs.writeFileSync(path.join(target, p.file), p.text);
    }
    const report = { scenes: plans.map(({ text, ...p }) => p), assets };
    fs.writeFileSync(path.join(target, 'Tools/ArtOptUiImportReport.json'), JSON.stringify(report, null, 2) + '\n');
    return report;
}
module.exports = { planScene, planAssets, apply, files, target, reference, parse };
if (require.main === module) console.log(JSON.stringify(apply(), null, 2));
