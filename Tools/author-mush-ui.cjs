// Writes ordinary serialized scene objects using the supplied prefab artwork.
// This is an authoring utility; it does not launch Unity or run validation.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const base = path.resolve(__dirname, '..');
const read = p => fs.readFileSync(path.join(base, p), 'utf8').replace(/\r\n/g, '\n');
const write = (p, s) => fs.writeFileSync(path.join(base, p), s, 'utf8');
const prefabs = 'Assets/UI_Panel_Sample/Prefab/';
function parse(s) {
    return [...s.matchAll(/^--- !u!(\d+) &(\d+)(?: stripped)?\n[\s\S]*?(?=^--- !u!|$(?![\s\S]))/gm)]
        .map(m => ({ type: m[1], id: m[2], text: m[0] }));
}
const field = (b, key) => b.text.match(new RegExp('^  ' + key + ': (.*)$', 'm'))?.[1];
const ref = (b, key) => field(b, key)?.match(/fileID: (\d+)/)?.[1];
const set = (b, key, value) => {
    const re = new RegExp('^  ' + key + ': .*$', 'm');
    if (re.test(b.text)) b.text = b.text.replace(re, () => '  ' + key + ': ' + value);
    else b.text += '  ' + key + ': ' + value + '\n';
};
function describe(p) {
    const bs = parse(read(p));
    const byId = new Map(bs.map(b => [b.id,b]));
    for (const b of bs.filter(b => b.type === '1')) {
        const t = bs.find(t => (t.type === '224' || t.type === '4') && ref(t,'m_GameObject') === b.id);
        const parent = byId.get(ref(t,'m_Father'));
        const go = parent && byId.get(ref(parent,'m_GameObject'));
        const comps = bs.filter(c => c.type === '114' && ref(c,'m_GameObject') === b.id);
        console.log(field(b,'m_Name'), 'parent='+ (go ? field(go,'m_Name') : 'ROOT'), field(t,'m_AnchoredPosition'), field(t,'m_SizeDelta'), comps.map(c=>field(c,'m_EditorClassIdentifier')?.split('::').at(-1)+(field(c,'m_text') ? ':'+field(c,'m_text') : '')).join(','));
    }
}
if (process.argv[2] === 'describe') {
    for (const name of ['TitleMenuUI','OptionUI','PauseUI','LobbyMenuUI','TrackUI','TrackProgressUI']) {
        console.log(name); describe(prefabs+name+'.prefab');
    }
    return;
}

const scriptNames = ['MushGameSave','MushAudioSettings','MushAudioChannel','MushSceneUI','MushCanvasQuestInput'];
const guids = {};
for (const name of scriptNames) {
    const p = 'Assets/Mush/Runtime/UI/' + name + '.cs.meta';
    if (!fs.existsSync(path.join(base,p))) write(p, 'fileFormatVersion: 2\nguid: '+crypto.randomBytes(16).toString('hex')+'\n');
    guids[name] = read(p).match(/guid: (\w+)/)[1];
}
guids.MushRideHud = read('Assets/Mush/Runtime/UI/MushRideHud.cs.meta').match(/guid: (\w+)/)[1];
const R = id => '{fileID: ' + (id || '0') + '}';
const common = '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n';
const buttonTemplate = parse(read(prefabs+'PauseUI.prefab')).find(b=>field(b,'m_EditorClassIdentifier')?.endsWith('UnityEngine.UI.Button'));
const imageTemplate = parse(read(prefabs+'TrackUI.prefab')).find(b=>field(b,'m_EditorClassIdentifier')?.endsWith('UnityEngine.UI.Image'));
const textTemplate = parse(read(prefabs+'TitleMenuUI.prefab')).find(b=>field(b,'m_EditorClassIdentifier')?.endsWith('TMPro.TextMeshProUGUI'));
class Author {
    constructor(existing=[]) { this.blocks=existing; this.next=88000000000000; this.roots=[]; }
    id() { return String(++this.next); }
    add(type, name, text, id=this.id()) { const b={type:String(type),id,text:'--- !u!'+type+' &'+id+'\n'+name+':\n'+text}; this.blocks.push(b); return b; }
    get(id) { return this.blocks.find(b=>b.id===id); }
    component(go, suffix) { return this.blocks.find(b=>ref(b,'m_GameObject')===go.id && field(b,'m_EditorClassIdentifier')?.endsWith(suffix)); }
    transform(go) { return this.blocks.find(b=>(b.type==='224'||b.type==='4')&&ref(b,'m_GameObject')===go.id); }
    addComponent(go, b) { go.text=go.text.replace(/(  m_Component:\n)/, '$1  - component: '+R(b.id)+'\n'); return b; }
    mono(go,guid,klass,fields='') { return this.addComponent(go,this.add(114,'MonoBehaviour', common+'  m_GameObject: '+R(go.id)+'\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: '+guid+', type: 3}\n  m_Name: \n  m_EditorClassIdentifier: '+klass+'\n'+fields)); }
    attach(t,parent) {
        set(t,'m_Father',R(parent?.id));
        if (parent) {
            parent.text=parent.text.replace('  m_Children: []','  m_Children:');
            parent.text=parent.text.replace(/(  m_Children:\n(?:  - \{fileID: \d+\}\n)*)/, '$1  - '+R(t.id)+'\n');
        } else this.roots.push(t.id);
    }
    empty(name,parent=null,size=[100,100],pos=[0,0],anchor=[0.5,0.5]) {
        const go=this.add(1,'GameObject',common+'  serializedVersion: 6\n  m_Component:\n  m_Layer: 5\n  m_Name: '+name+'\n  m_TagString: Untagged\n  m_Icon: {fileID: 0}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n');
        const t=this.addComponent(go,this.add(224,'RectTransform',common+'  m_GameObject: '+R(go.id)+'\n  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_LocalPosition: {x: 0, y: 0, z: 0}\n  m_LocalScale: {x: 1, y: 1, z: 1}\n  m_ConstrainProportionsScale: 0\n  m_Children: []\n  m_Father: {fileID: 0}\n  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n  m_AnchorMin: {x: 0.5, y: 0.5}\n  m_AnchorMax: {x: 0.5, y: 0.5}\n  m_AnchoredPosition: {x: 0, y: 0}\n  m_SizeDelta: {x: 100, y: 100}\n  m_Pivot: {x: 0.5, y: 0.5}\n'));
        this.attach(t,parent); this.place(t,size,pos,anchor); return go;
    }
    place(t,size,pos=[0,0],anchor=[0.5,0.5],scale=1) {
        set(t,'m_AnchorMin',`{x: ${anchor[0]}, y: ${anchor[1]}}`); set(t,'m_AnchorMax',field(t,'m_AnchorMin'));
        set(t,'m_SizeDelta',`{x: ${size[0]}, y: ${size[1]}}`); set(t,'m_AnchoredPosition',`{x: ${pos[0]}, y: ${pos[1]}}`);
        set(t,'m_LocalScale',`{x: ${scale}, y: ${scale}, z: ${scale}}`);
    }
    stretch(t) { this.place(t,[0,0]); set(t,'m_AnchorMin','{x: 0, y: 0}'); set(t,'m_AnchorMax','{x: 1, y: 1}'); }
    template(go,source) {
        const id=this.id(); const b={...source,id,text:source.text.replace(/^--- !u!(\d+) &\d+/, '--- !u!$1 &'+id)};
        set(b,'m_GameObject',R(go.id)); this.blocks.push(b); this.addComponent(go,b); return b;
    }
    renderer(go) { this.addComponent(go,this.add(222,'CanvasRenderer',common+'  m_GameObject: '+R(go.id)+'\n  m_CullTransparentMesh: 1\n')); }
    image(go,color='{r: 1, g: 1, b: 1, a: 1}',raycast=0) {
        this.renderer(go); const c=this.template(go,imageTemplate); set(c,'m_Sprite',R(0)); set(c,'m_Type','0'); set(c,'m_Color',color); set(c,'m_RaycastTarget',raycast); return c;
    }
    text(name,parent,value,size,pos,fontSize=28,anchor=[0.5,0.5]) {
        const go=this.empty(name,parent,size,pos,anchor); this.renderer(go); const c=this.template(go,textTemplate);
        c.text=c.text.replace(/  m_text: [\s\S]*?(?=  m_isRightToLeft:)/, '  m_text: '+JSON.stringify(value)+'\n');
        set(c,'m_fontSize',fontSize);set(c,'m_fontSizeBase',fontSize);set(c,'m_RaycastTarget',0);set(c,'m_enableAutoSizing',0);set(c,'m_TextWrappingMode',0);
        return {go,t:this.transform(go),c};
    }
    button(go,target) { let c=this.component(go,'UnityEngine.UI.Button'); if (!c) c=this.template(go,buttonTemplate); set(c,'m_TargetGraphic',R(target.id)); set(target,'m_RaycastTarget',1); return c; }
    clone(name,parent) {
        const source=parse(read(prefabs+name+'.prefab')); const ids=new Map(source.map(b=>[b.id,this.id()]));
        const blocks=source.map(b=>({...b,id:ids.get(b.id),text:b.text.replace(/^--- !u!(\d+) &(\d+)/,(_,t,id)=>'--- !u!'+t+' &'+ids.get(id)).replace(/\{fileID: (\d+)\}/g,(all,id)=>ids.has(id)?R(ids.get(id)):all)}));
        this.blocks.push(...blocks);
        const t=blocks.find(b=>b.type==='224'&&ref(b,'m_Father')==='0'); const root=this.get(ref(t,'m_GameObject'));
        this.attach(t,parent);
        const byName=name=>blocks.find(b=>b.type==='1'&&field(b,'m_Name')===name);
        // Explicit positions are authored below, so sample auto-layout never changes the edit-time layout.
        for (const b of blocks) {
            if (field(b,'m_EditorClassIdentifier')?.endsWith('LayoutGroup')) set(b,'m_Enabled',0);
            if (field(b,'m_RaycastTarget')!==undefined) set(b,'m_RaycastTarget',0);
        }
        return {root,t,blocks,byName};
    }
    descendants(go) {
        const out=[go]; const t=this.transform(go);
        for (const child of this.blocks.filter(b=>(b.type==='224'||b.type==='4')&&ref(b,'m_Father')===t.id)) out.push(...this.descendants(this.get(ref(child,'m_GameObject'))));
        return out;
    }
    firstImage(go) {
        const own=this.component(go,'UnityEngine.UI.Image');if(own)return own;
        const children=this.descendants(go);
        const outside=children.find(child=>field(child,'m_Name')==='Image_Outside');
        if(outside)return this.component(outside,'UnityEngine.UI.Image');
        for (const child of children) { const c=this.component(child,'UnityEngine.UI.Image'); if(c) return c; }
    }
    menuButton(name,label,parent,pos,scale=1) {
        const p=this.clone('LobbyMenuUI',parent); set(p.root,'m_Name',name); this.place(p.t,[200,100],pos,[0.5,0.5],scale);
        const tx=this.component(p.byName('UI_Text'),'TMPro.TextMeshProUGUI');
        set(tx,'m_text',JSON.stringify(label)); set(tx,'m_fontSize',28);set(tx,'m_fontSizeBase',28);set(tx,'m_TextWrappingMode',0);
        this.button(p.root,this.firstImage(p.root)); return p;
    }
    modal(name,canvas) {
        const root=this.empty(name,canvas); const t=this.transform(root); this.stretch(t);
        const back=this.empty('Modal Background',t);this.stretch(this.transform(back));this.image(back,'{r: 0.015, g: 0.025, b: 0.035, a: 0.72}',1);
        return {root,t};
    }
    options(canvas) {
        const wrapper=this.modal('OptionUI',canvas); const p=this.clone('OptionUI',wrapper.t);set(p.root,'m_Name','OptionUI_Content');this.place(p.t,[800,400]);
        const list=p.byName('Options_List');this.place(this.transform(list),[680,210],[0,-32]);
        const slots={};
        ['01_MasterVolume','02_BGM','03_SE'].forEach((name,i)=>{
            const row=p.byName(name);this.place(this.transform(row),[680,60],[0,70-i*70]);
            const members=this.descendants(row);const label=members.find(b=>field(b,'m_Name')==='Option_Text');const count=members.find(b=>field(b,'m_Name')==='Count');const slider=members.find(b=>field(b,'m_Name')==='Slider');
            this.place(this.transform(label),[240,55],[-215,0]);this.place(this.transform(slider),[280,30],[65,0]);this.place(this.transform(count),[70,55],[285,0]);
            const sliderComponent=this.component(slider,'UnityEngine.UI.Slider');set(sliderComponent,'m_MinValue',0);set(sliderComponent,'m_MaxValue',1);set(sliderComponent,'m_WholeNumbers',0);set(sliderComponent,'m_Value',1);
            for(const c of this.descendants(slider)) {const im=this.component(c,'UnityEngine.UI.Image');if(im)set(im,'m_RaycastTarget',1);}
            for(const go of [label,count]) {const txt=this.component(go,'TMPro.TextMeshProUGUI');set(txt,'m_fontSize',28);set(txt,'m_fontSizeBase',28);}
            slots[['master','music','effects'][i]]=sliderComponent.id;slots[['masterCount','musicCount','effectsCount'][i]]=this.component(count,'TMPro.TextMeshProUGUI').id;
        });
        const close=p.byName('Image_Back');set(close,'m_Name','Button_Close');this.place(this.transform(close),[48,48],[335,135]);this.button(close,this.firstImage(close));
        set(wrapper.root,'m_IsActive',0);return {...wrapper,slots};
    }
    pause(canvas) {
        const wrapper=this.modal('PauseUI',canvas);const p=this.clone('PauseUI',wrapper.t);set(p.root,'m_Name','PauseUI_Content');this.place(p.t,[660,440]);
        this.place(this.transform(p.byName('Options_List')),[560,220],[0,-25]);
        ['Button_Resume','Button_Option','Button_Lobby','Button_Quit'].forEach((name,i)=>{const go=p.byName(name);this.place(this.transform(go),[250,80],[i%2===0?-140:140,i<2?55:-55]);this.button(go,this.firstImage(go));});
        set(p.byName('Image_Back'),'m_IsActive',0);this.pauseControls(wrapper,p);set(wrapper.root,'m_IsActive',0);return wrapper;
    }
    pauseControls(wrapper,p) {
        this.place(this.transform(p.byName('Image_Outside')),[660,550]);
        this.place(this.transform(p.byName('Image_Inside')),[620,510]);
        this.place(this.transform(p.byName('UI_Text')),[280,100],[0,185]);
        this.place(this.transform(p.byName('Options_List')),[560,220],[0,30]);
        const recovery=this.menuButton('Button_Recover','코스 복귀 (Z)',p.t,[0,-180],1.1);
        const label=this.component(recovery.byName('UI_Text'),'TMPro.TextMeshProUGUI');
        set(label,'m_fontSize',24);set(label,'m_fontSizeBase',24);
        this.text('Pause Controls',wrapper.t,'SPACE 출발 · W 가속 · A / D 조향\nESC 일시정지 / 재개\nZ 코스 복귀 (아래 코스 복귀 버튼으로도 가능)\nQuest: B 일시정지 · 체력 0: 속도·가속력 감소\nN Pos 재보정: 일시정지 중 그립을 놓았다가 양손 그립 1초',[1100,180],[0,385],26);
    }
    mountTrack(p,ride) {
        const timer=this.get(ref(ride,'missionTimerRoot'));
        const timerTransform=this.transform(timer);
        const seat=this.get(ref(timerTransform,'m_Father'));
        const previousParent=this.get(ref(p.t,'m_Father'));
        if(previousParent)previousParent.text=previousParent.text.replace('  - '+R(p.t.id)+'\n','');
        this.attach(p.t,seat);
        this.place(p.t,[300,180],[0,0],[0.5,0.5],0.002);
        set(p.t,'m_LocalPosition',field(timerTransform,'m_LocalPosition'));
        const position=field(timerTransform,'m_LocalPosition').match(/x: ([^,]+), y: ([^,]+)/);
        set(p.t,'m_AnchoredPosition',`{x: ${position[1]}, y: ${position[2]}}`);
        set(p.t,'m_LocalRotation',field(timerTransform,'m_LocalRotation'));
        this.place(this.transform(p.byName('TrackUI_Panel')),[300,180],[0,0]);
        const screenCanvas=this.blocks.find(b=>b.type==='223'&&field(b,'m_RenderMode')==='1');
        const canvas=this.template(p.root,screenCanvas);
        set(canvas,'m_RenderMode',2);set(canvas,'m_Camera',field(ride,'rideCamera'));
        set(canvas,'m_OverrideSorting',0);set(canvas,'m_SortingOrder',0);
        set(timer,'m_IsActive',0);
    }
    title(canvas) {
        const p=this.clone('TitleMenuUI',canvas);this.place(p.t,[400,640],[0,-40]);
        ['TitleUI_Start','TitleUI_Continue','TitleUI_Option','TitleUI_Quit'].forEach((name,i)=>{const go=p.byName(name);this.place(this.transform(go),[350,140],[0,240-i*160]);this.button(go,this.firstImage(go));});
        this.text('Game Title',canvas,'MUSH',[900,160],[0,405],112);
        this.text('Subtitle',canvas,'YOUR TRAIL BEGINS HERE',[1000,50],[0,300],26);
        return p;
    }
    hud(canvas,ride) {
        const p=this.clone('TrackUI',canvas);this.place(p.t,[100,100],[0,20],[0.5,0]);set(p.byName('DogStateImage_Fast'),'m_IsActive',0);
        const trackText=this.component(p.byName('TimerText'),'TMPro.TextMeshProUGUI');set(trackText,'m_text','"02:00"');
        const stamina=this.component(p.byName('FilledImage'),'UnityEngine.UI.Image');
        set(stamina,'m_Type',3);set(stamina,'m_FillMethod',0);set(stamina,'m_FillOrigin',0);set(stamina,'m_FillAmount',1);
        this.mountTrack(p,ride);
        const bar=this.clone('TrackProgressUI',canvas);this.place(bar.t,[100,100],[0,-30],[0.5,1]);
        this.place(this.transform(bar.byName('ProgressImage')),[1000,48],[0,-20]);
        const fill=this.component(bar.byName('ProgressBar'),'UnityEngine.UI.Image');set(fill,'m_Type',3);set(fill,'m_FillMethod',0);set(fill,'m_FillOrigin',0);set(fill,'m_FillAmount',0);
        this.place(this.transform(bar.byName('ProgressBar')),[952,26]);this.place(this.transform(bar.byName('ProgressIcon')),[42,42],[-476,0]);
        const go=this.get(ref(canvas,'m_GameObject'));
        this.mono(go,guids.MushRideHud,'Assembly-CSharp::MushRideHud',Object.entries({ride:ride.id,timer:trackText.id,progress:fill.id,staminaFill:stamina.id,progressIcon:this.transform(bar.byName('ProgressIcon')).id,trackRoot:p.root.id,progressRoot:bar.root.id,standingDog:p.byName('DogStateImage_Normal').id,runningDog:this.transform(p.byName('DogStateImage_Fast')).id}).map(([k,v])=>'  '+k+': '+R(v)+'\n').join(''));
        return {track:p,bar};
    }
    lobbyStaminaHud(canvas) {
        const root=this.empty('Lobby Stamina HUD',canvas,[460,120],[0,-78],[0.5,1]);
        const t=this.transform(root);
        const source=parse(read(prefabs+'TrackUI.prefab'));
        const sampleImage=name=>{
            const go=source.find(b=>b.type==='1'&&field(b,'m_Name')===name);
            return source.find(b=>ref(b,'m_GameObject')===go.id&&field(b,'m_EditorClassIdentifier')?.endsWith('UnityEngine.UI.Image'));
        };
        const background=this.empty('Stamina Background',t,[380,90],[0,-16]);
        this.renderer(background);
        const backgroundImage=this.template(background,sampleImage('DogHealthBar'));
        set(backgroundImage,'m_RaycastTarget',0);
        const fillGo=this.empty('Stamina Fill',this.transform(background),[380,90]);
        this.renderer(fillGo);
        const fill=this.template(fillGo,sampleImage('FilledImage'));
        set(fill,'m_Type',3);set(fill,'m_FillMethod',0);set(fill,'m_FillOrigin',0);set(fill,'m_FillAmount',1);set(fill,'m_RaycastTarget',0);
        const label=this.text('Stamina Text',t,'썰매견 체력  100 / 100',[460,40],[0,38],26);
        const guid=read('Assets/Mush/Runtime/UI/MushLobbyStaminaHud.cs.meta').match(/guid: (\w+)/)[1];
        this.mono(root,guid,'Assembly-CSharp::MushLobbyStaminaHud','  staminaFill: '+R(fill.id)+'\n  staminaText: '+R(label.c.id)+'\n');
        this.lobbyConditionIcon(root);
        return root;
    }
    lobbyConditionIcon(root) {
        const t=this.transform(root);
        const icon=this.empty('Dog Condition Icon',t,[64,64],[244,-4]);
        this.renderer(icon);
        const guid=read('Assets/Mush/Runtime/UI/MushDogConditionIcon.cs.meta').match(/guid: (\w+)/)[1];
        const graphic=this.mono(icon,guid,'Assembly-CSharp::MushDogConditionIcon',
            '  m_Material: {fileID: 0}\n  m_Color: {r: 1, g: 1, b: 1, a: 1}\n  m_RaycastTarget: 0\n  m_Maskable: 1\n  condition: 0\n');
        const label=this.text('Dog Condition Text',t,'평범',[100,32],[244,-52],22);
        const hud=this.component(root,'MushLobbyStaminaHud');
        set(hud,'conditionIcon',R(graphic.id));set(hud,'conditionText',R(label.c.id));
    }
    canvas(camera) {
        const go=this.empty('Mush Scene UI');const t=this.transform(go);this.place(t,[1920,1080]);
        const c=this.addComponent(go,this.add(223,'Canvas',common+'  m_GameObject: '+R(go.id)+'\n  m_Enabled: 1\n  serializedVersion: 3\n  m_RenderMode: 1\n  m_Camera: '+R(camera.id)+'\n  m_PlaneDistance: 2\n  m_PixelPerfect: 0\n  m_ReceivesEvents: 1\n  m_OverrideSorting: 0\n  m_OverridePixelPerfect: 0\n  m_SortingBucketNormalizedSize: 0\n  m_VertexColorAlwaysGammaSpace: 1\n  m_AdditionalShaderChannelsFlag: 25\n  m_UpdateRectTransformForStandalone: 0\n  m_SortingLayerID: 0\n  m_SortingOrder: 100\n  m_TargetDisplay: 0\n'));
        this.mono(go,'0cd44c1031e13a943bb63640046fad76','UnityEngine.UI::UnityEngine.UI.CanvasScaler','  m_UiScaleMode: 1\n  m_ReferencePixelsPerUnit: 100\n  m_ScaleFactor: 1\n  m_ReferenceResolution: {x: 1920, y: 1080}\n  m_ScreenMatchMode: 0\n  m_MatchWidthOrHeight: 0.5\n  m_PhysicalUnit: 3\n  m_FallbackScreenDPI: 96\n  m_DefaultSpriteDPI: 96\n  m_DynamicPixelsPerUnit: 1\n  m_PresetInfoIsWorld: 0\n');
        const ray=this.mono(go,'dc42784cf147c0c48a680349fa168899','UnityEngine.UI::UnityEngine.UI.GraphicRaycaster','  m_IgnoreReversedGraphics: 1\n  m_BlockingObjects: 0\n  m_BlockingMask:\n    serializedVersion: 2\n    m_Bits: 4294967295\n');
        this.mono(go,guids.MushCanvasQuestInput,'Assembly-CSharp::MushCanvasQuestInput','  canvas: '+R(c.id)+'\n  raycaster: '+R(ray.id)+'\n');
        const eventGo=this.empty('EventSystem',t);
        this.mono(eventGo,'76c392e42b5098c458856cdf6ecaaaa1','UnityEngine.UI::UnityEngine.EventSystems.EventSystem','  m_FirstSelected: {fileID: 0}\n  m_sendNavigationEvents: 1\n  m_DragThreshold: 10\n');
        this.mono(eventGo,'01614664b831546d2ae94a42149d80ac','Unity.InputSystem::UnityEngine.InputSystem.UI.InputSystemUIInputModule','  m_SendPointerHoverToParent: 1\n  m_MoveRepeatDelay: 0.5\n  m_MoveRepeatRate: 0.1\n  m_XRTrackingOrigin: {fileID: 0}\n  m_DeselectOnBackgroundClick: 1\n  m_PointerBehavior: 0\n  m_CursorLockBehavior: 0\n  m_ScrollDeltaPerTick: 6\n');
        return {go,t};
    }
    serialize() {
        let roots=this.blocks.find(b=>b.type==='1660057539');
        if(!roots) roots=this.add('1660057539','SceneRoots','  m_ObjectHideFlags: 0\n  m_Roots:\n','9223372036854775807');
        roots.text=roots.text.replace('  m_Roots: []','  m_Roots:');
        roots.text=roots.text.trimEnd()+'\n'+this.roots.map(id=>'  - '+R(id)+'\n').join('');
        return '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'+this.blocks.map(b=>b.text).join('');
    }
}

if (require.main !== module) {
    module.exports = { Author, parse, field, ref, set, R };
    return;
}

if (process.argv[2] === 'lobby-stamina' || process.argv[2] === 'lobby-condition') {
    const scenePath='Assets/Mush/Scenes/MushLobby.unity';
    const a=new Author(parse(read(scenePath)));
    a.next=Math.max(a.next,...a.blocks.filter(b=>BigInt(b.id)>88000000000000n&&BigInt(b.id)<88000000100000n).map(b=>Number(b.id)));
    const existing=a.blocks.find(b=>b.type==='1'&&field(b,'m_Name')==='Lobby Stamina HUD');
    if(existing) {
        if(a.descendants(existing).some(b=>field(b,'m_Name')==='Dog Condition Icon')) return;
        a.lobbyConditionIcon(existing);
    } else {
        const canvas=a.blocks.find(b=>b.type==='1'&&field(b,'m_Name')==='Mush Scene UI');
        a.lobbyStaminaHud(a.transform(canvas));
    }
    write(scenePath,a.serialize());
    console.log('Authored lobby stamina HUD');
    return;
}

function deactivateOldHud(a) {
    for(const hud of a.blocks.filter(b=>field(b,'m_EditorClassIdentifier')==='Assembly-CSharp::MushRideHud')) {
        set(hud,'m_Enabled',0);
        for(const key of ['trackRoot','progressRoot']) {
            const go=a.get(ref(hud,key));if(!go)continue;
            const instance=a.get(ref(go,'m_PrefabInstance'));
            if(instance) {
                const source=field(go,'m_CorrespondingSourceObject');
                const entry='    - target: '+source+'\n      propertyPath: m_IsActive\n      value: 0\n      objectReference: {fileID: 0}\n';
                if(!instance.text.includes(entry)) instance.text=instance.text.replace('    m_Modifications:\n','    m_Modifications:\n'+entry);
            } else set(go,'m_IsActive',0);
        }
    }
}
function newTitle() {
    const source=parse(read('Assets/Mush/Scenes/snow.unity'));
    const a=new Author(source.filter(b=>['29','104','157','196'].includes(b.type)));
    const go=a.empty('Main Camera');set(go,'m_TagString','MainCamera');set(go,'m_Layer',0);
    const t=a.transform(go);t.type='4';t.text=t.text.replace('!u!224','!u!4').replace('RectTransform:','Transform:').replace(/^  m_(AnchorMin|AnchorMax|AnchoredPosition|SizeDelta|Pivot):.*\n/gm,'');set(t,'m_LocalPosition','{x: 0, y: 0, z: -10}');
    const camera=a.template(go,source.find(b=>b.type==='20'));set(camera,'m_ClearFlags',2);set(camera,'m_BackGroundColor','{r: 0.055, g: 0.105, b: 0.14, a: 1}');
    a.addComponent(go,a.add(81,'AudioListener',common+'  m_GameObject: '+R(go.id)+'\n  m_Enabled: 1\n'));
    return {a,camera};
}
for(const scene of ['MushTitle','MushLobby','snow','Tree','SharpCurve']) {
    const scenePath='Assets/Mush/Scenes/'+scene+'.unity';
    let a,camera;
    if(scene==='MushTitle') ({a,camera}=newTitle());
    else {
        const original=parse(read(scenePath)).filter(b=>!(BigInt(b.id)>88000000000000n && BigInt(b.id)<88000000100000n));
        for(const block of original.filter(b=>['1660057539','4','224'].includes(b.type)))
            block.text=block.text.replace(/^  - \{fileID: (\d+)\}\n/gm,(line,id)=>BigInt(id)>88000000000000n&&BigInt(id)<88000000100000n?'':line);
        a=new Author(original);
        camera=a.blocks.find(b=>b.type==='20' && field(a.get(ref(b,'m_GameObject')),'m_TagString')==='MainCamera') || a.blocks.find(b=>b.type==='20');
        // Store and housing author their own camera at runtime; use the scene UI's own preview camera.
        if(!camera) {
            const source=newTitle(); const camGo=source.a.get(ref(source.camera,'m_GameObject'));
            const camT=source.a.transform(camGo);
            const group=[camGo,camT,...source.a.blocks.filter(b=>ref(b,'m_GameObject')===camGo.id && b.id!==camT.id)];
            a.blocks.push(...group);a.next=source.a.next;a.roots.push(camT.id);camera=source.camera;
            set(camGo,'m_Name','UI Preview Camera');set(camGo,'m_TagString','Untagged');
        }
        deactivateOldHud(a);
    }
    const ride=a.blocks.find(b=>field(b,'m_EditorClassIdentifier')==='Assembly-CSharp::MushMapRideBootstrap');
    const canvas=a.canvas(camera);
    const refs={ride:ride?.id};
    if(scene==='MushLobby') a.lobbyStaminaHud(canvas.t);
    if(scene==='MushTitle') refs.titlePanel=a.title(canvas.t).root.id;
    if(ride) {a.hud(canvas.t,ride);refs.pausePanel=a.pause(canvas.t).root.id;}
    if(scene!=='MushLobby') {
        const options=a.options(canvas.t);refs.optionPanel=options.root.id;Object.assign(refs,options.slots);
        refs.message=a.text('Save Status',canvas.t,'',[1100,46],[0,20],25,[0.5,0]).c.id;
    }
    a.mono(canvas.go,guids.MushSceneUI,'Assembly-CSharp::MushSceneUI',Object.entries(refs).map(([k,v])=>'  '+k+': '+R(v)+'\n').join(''));
    write(scenePath,a.serialize());
    if(!fs.existsSync(path.join(base,scenePath+'.meta')))write(scenePath+'.meta','fileFormatVersion: 2\nguid: '+crypto.randomBytes(16).toString('hex')+'\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n');
    console.log('Authored '+scenePath);
}
const titleGuid=read('Assets/Mush/Scenes/MushTitle.unity.meta').match(/guid: (\w+)/)[1];
let build=read('ProjectSettings/EditorBuildSettings.asset');
if(!build.includes('path: Assets/Mush/Scenes/MushTitle.unity')) build=build.replace('  m_Scenes:\n','  m_Scenes:\n  - enabled: 1\n    path: Assets/Mush/Scenes/MushTitle.unity\n    guid: '+titleGuid+'\n');
write('ProjectSettings/EditorBuildSettings.asset',build);

// A separate editable display keeps every modal visible without covering normal gameplay scenes.
{
    const {a,camera}=newTitle();const canvas=a.canvas(camera);
    const titleFrame=a.empty('TitleMenuUI Preview',canvas.t,[400,800],[-650,-25]);
    a.place(a.transform(titleFrame),[400,800],[-650,-25],[0.5,0.5],0.7);a.title(a.transform(titleFrame));
    const options=a.options(canvas.t);set(options.root,'m_IsActive',1);a.place(options.t,[800,400],[240,270],[0.5,0.5],0.8);
    const pause=a.pause(canvas.t);set(pause.root,'m_IsActive',1);a.place(pause.t,[660,440],[240,-140],[0.5,0.5],0.75);
    a.menuButton('LobbyMenuUI Preview','Lobby Menu',canvas.t,[710,-370]);
    const track=a.clone('TrackUI',canvas.t);a.place(track.t,[100,100],[-130,-500]);set(track.byName('DogStateImage_Fast'),'m_IsActive',0);
    const bar=a.clone('TrackProgressUI',canvas.t);a.place(bar.t,[100,100],[220,-430]);
    a.text('Preview Note',canvas.t,'UI PANEL PREVIEW  /  EDITABLE SCENE OBJECTS',[1600,50],[0,505],28);
    const previewPath='Assets/Mush/Scenes/MushUIPreview.unity';write(previewPath,a.serialize());
    if(!fs.existsSync(path.join(base,previewPath+'.meta')))write(previewPath+'.meta','fileFormatVersion: 2\nguid: '+crypto.randomBytes(16).toString('hex')+'\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n');
    console.log('Authored '+previewPath);
}
