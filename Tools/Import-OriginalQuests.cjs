'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
// Parse a deliberately bounded declarative Lua subset; never execute supplied Lua.
function cleanLua(s) {
 let out='', quote=null;
 for(let i=0;i<s.length;i++) {
  const c=s[i];
  if(quote) { out+=c; if(c==='\\') out+=s[++i]||''; else if(c===quote) quote=null; }
  else if(c==='"'||c==="'") {quote=c;out+=c;}
  else if(c==='-'&&s[i+1]==='-') {
   if(s.slice(i+2,i+4)==='[[') {let j=s.indexOf(']]',i+4);i=j<0?s.length:j+1;}
   else {while(i<s.length&&s[i]!=='\n')i++;out+='\n';}
  } else out+=c;
 }
 return out;
}
function calls(s) {
 const result=[];let re=/\b([A-Za-z_]\w*)\s*\(/g,m;
 while((m=re.exec(s))) {
  let i=re.lastIndex,depth=1,q=null,buf='',args=[];
  for(;i<s.length&&depth;i++) {let c=s[i];if(q){buf+=c;if(c==='\\')buf+=s[++i]||'';else if(c===q)q=null;}
   else if(c==='"'||c==="'"){q=c;buf+=c;}
   else if(c==='('||c==='{'){depth++;buf+=c;}else if(c==='}'){depth--;buf+=c;}else if(c===')'){if(--depth)buf+=c;}
   else if(c===','&&depth===1){args.push(buf.trim());buf='';}else buf+=c;
  }
  if(buf.trim()||args.length)args.push(buf.trim());result.push({name:m[1],args,start:m.index,end:i});re.lastIndex=i;
 }
 return result;
}
function unquote(s){if(!s)return '';if(s[0]!=="'"&&s[0]!=='"')return s;return s.slice(1,-1).replace(/\\(['"\\])/g,'$1').replace(/\\n/g,'\n');}
const conditions = new Set('AlwaysFailure AlwaysSuccess NoMission HasMission NoRecord HasRecord HasFlag NoFlag LvCheck PfEqual NoPfEqual HasItem NoItem HasMoney CTypeCheck IsChaType NoChaType CheckConvertProfession'.split(' '));
const actions = new Set('AddMission ClearMission SetFlag ClearFlag SetRecord ClearRecord GiveItem TakeItem AddExp AddMoney TakeMoney SetProfession ClearFightSkill AddTrigger ClearTrigger SystemNotice'.split(' '));
const harmless = new Set('MisBeginTalk MisResultTalk MisHelpTalk MisNeed MisPrize MisPrizeSelAll MisBeginBagNeed MisResultBagNeed InitTrigger TriggerCondition TriggerAction RegCurTrigger'.split(' '));
function importCatalog(sourceDir) {
 const sdk=cleanLua(fs.readFileSync(path.join(sourceDir,'..','MisSdk','MissionSdk.lua'),'utf8'));let constants={TE_KILL:1,TE_GETITEM:2,COMPLETE_SHOW:1};
 for(let m of sdk.matchAll(/^\s*(\w+)\s*=\s*(\d+)\s*$/gm))constants[m[1]]=+m[2];
 const resolve=x=>/^[+-]?\d+$/.test(x)?+x:constants[x]!==undefined?constants[x]:null;
 const files=fs.readdirSync(sourceDir).filter(x=>/^MissionScript\d+\.lua$/.test(x)).sort();
 let definitions=[],unsupported=[],hashes={},npcBindings=new Map();
 const npcs=fs.readdirSync(sourceDir).filter(x=>/^NpcScript\d+\.lua$/.test(x));let npcFunctions=new Map();
 for(let f of npcs){let s=cleanLua(fs.readFileSync(path.join(sourceDir,f),'utf8'));let matches=[...s.matchAll(/^\s*function\s+(\w+)\s*\([^\n]*\)/gm)];for(let i=0;i<matches.length;i++){let body=s.slice(matches[i].index,i+1<matches.length?matches[i+1].index:s.length);npcFunctions.set(matches[i][1],calls(body).filter(c=>c.name==='AddNpcMission').map(c=>resolve(c.args[0])));}}
 const resource=path.resolve(sourceDir,'..','..');
 for(let dir of fs.readdirSync(resource,{withFileTypes:true}).filter(d=>d.isDirectory())) {
  let file=path.join(resource,dir.name,dir.name+'npc.txt');if(!fs.existsSync(file))continue;
  for(let line of fs.readFileSync(file,'utf8').split(/\r?\n/)){if(/^\s*\/\//.test(line))continue;let row=line.split('\t');if(row.length<12)continue;for(let id of npcFunctions.get(row[11].trim())||[]){if(!npcBindings.has(id))npcBindings.set(id,new Set());npcBindings.get(id).add(row[1].trim());}}
 }
 for(let f of files) {
  let raw=fs.readFileSync(path.join(sourceDir,f)),s=cleanLua(raw.toString('utf8'));hashes[f]=crypto.createHash('sha256').update(raw).digest('hex');let cs=calls(s),starts=cs.map((c,i)=>({c,i})).filter(x=>['DefineMission','DefineRandMission'].includes(x.c.name));
  for(let index=0;index<starts.length;index++) {
   let {c,i}=starts[index],end=index+1<starts.length?starts[index+1].i:cs.length,block=cs.slice(i+1,end);
   // A function boundary terminates the block, preventing calls from adjacent unrelated functions.
   let boundary=s.slice(c.end,index+1<starts.length?starts[index+1].c.start:s.length).search(/\n\s*function\s/);if(boundary>=0)block=block.filter(x=>x.start<c.end+boundary);
   let id=resolve(c.args[0]),reasons=new Set();let def={Id:id,MissionId:resolve(c.args[2])||id,Name:unquote(c.args[1]),Source:f,NpcNames:[...(npcBindings.get(id)||[])],CompletionOnly:resolve(c.args[3])===1,BeginConditions:[],ResultConditions:[],BeginActions:[],ResultActions:[],CancelActions:[],Needs:[],Triggers:[],BeginTalk:'',ResultTalk:'',BeginBagNeed:0,ResultBagNeed:0};
   if(c.name==='DefineRandMission')reasons.add('random mission generator');
   const command=(args)=>({Name:args[0],Args:(args[0]==='CTypeCheck'?args.slice(1).join(',').replace(/[{}]/g,'').split(','):args.slice(1)).map(x=>String(resolve(x)===null?unquote(x):resolve(x)))});
   let trigger=null;
   for(let call of block) {
    let n=call.name,a=call.args;
    if(/^Mis(Begin|Result)Condition$/.test(n)){if(!conditions.has(a[0]))reasons.add('condition '+a[0]);def[n==='MisBeginCondition'?'BeginConditions':'ResultConditions'].push(command(a));}
    else if(/^Mis(Begin|Result|Cancel)Action$/.test(n)){if(!actions.has(a[0]))reasons.add('action '+a[0]);let cmd=command(a);if(cmd.Args.some((x,j)=>a[0]!=='SystemNotice'&&resolve(a[j+1])===null))reasons.add('dynamic action '+a[0]);def[n==='MisBeginAction'?'BeginActions':n==='MisResultAction'?'ResultActions':'CancelActions'].push(cmd);}
    else if(n==='MisBeginTalk'||n==='MisResultTalk')def[n==='MisBeginTalk'?'BeginTalk':'ResultTalk']=unquote(a[0]);
    else if(n==='MisBeginBagNeed'||n==='MisResultBagNeed')def[n==='MisBeginBagNeed'?'BeginBagNeed':'ResultBagNeed']=resolve(a[0]);
    else if(n==='MisNeed'){if(!['MIS_NEED_ITEM','MIS_NEED_KILL','MIS_NEED_DESP'].includes(a[0]))reasons.add('need '+a[0]);def.Needs.push({Type:a[0],Target:resolve(a[1])||0,Count:resolve(a[2])||1,Flag:resolve(a[3])||0,Description:unquote(a[1])});}
    else if(n==='MisPrize'){if(!['MIS_PRIZE_ITEM','MIS_PRIZE_MONEY'].includes(a[0]))reasons.add('prize '+a[0]);else def.ResultActions.push({Name:a[0]==='MIS_PRIZE_ITEM'?'GiveItem':'AddMoney',Args:a.slice(1).map(x=>String(resolve(x)))});}
    else if(n==='InitTrigger')trigger={};
    else if(n==='TriggerCondition'){if(a[1]==='IsMonster'||a[1]==='IsItem'){if(!trigger)trigger={};trigger.Type=a[1];trigger.Target=resolve(a[2]);}else reasons.add('trigger condition '+a[1]);}
    else if(n==='TriggerAction'){if(a[1]==='AddNextFlag'){if(!trigger)trigger={};trigger.MissionId=resolve(a[2]);trigger.Flag=resolve(a[3]);trigger.Count=resolve(a[4]);}else reasons.add('trigger action '+a[1]);}
    else if(n==='RegCurTrigger'){if(!trigger||!trigger.Type||!trigger.MissionId||!trigger.Count)reasons.add('incomplete trigger');else def.Triggers.push({...trigger,Id:resolve(a[0])});trigger=null;}
    else if(n==='MisPrizeSelOne')reasons.add('choice reward');
    else if(n.startsWith('Mis')&&!harmless.has(n))reasons.add('command '+n);
   }
   if(!Number.isInteger(id)||id<=0)reasons.add('dynamic definition id');
   if(def.BeginConditions.length===0&&def.BeginActions.length===0)reasons.add('empty mission');
   if(def.NpcNames.length===0)reasons.add('not bound to an original NPC');
   for(let cmd of [...def.BeginConditions,...def.ResultConditions]){let numeric=cmd.Name==='LvCheck'?cmd.Args.slice(1):cmd.Args;if(numeric.some(x=>!/^\d+$/.test(x)))reasons.add('dynamic condition '+cmd.Name);}
   // Functions/branches inside declarative blocks require a real Lua VM; fail closed.
   let body=s.slice(c.end,block.length?block[block.length-1].end:c.end);if(/^\s*(?:if|elseif|for|while|local)\b/m.test(body))reasons.add('dynamic Lua control flow');
   if(reasons.size)unsupported.push({Id:id,Name:def.Name,Source:f,Reasons:[...reasons].sort()});else definitions.push(def);
  }
 }
 const itemIds=new Set(fs.readFileSync(path.join(__dirname,'..','Assets','Resources','PKO','iteminfo.txt'),'utf8').split(/\r?\n/).map(l=>l.split('\t')[0]).filter(x=>/^\d+$/.test(x)).map(Number));
 definitions=definitions.filter(d=>{let missing=[...new Set([...d.BeginActions,...d.ResultActions,...d.CancelActions,...d.BeginConditions,...d.ResultConditions].filter(a=>['GiveItem','TakeItem','HasItem','NoItem'].includes(a.Name)&&!itemIds.has(+a.Args[0])).map(a=>+a.Args[0]))];if(!missing.length)return true;unsupported.push({Id:d.Id,Name:d.Name,Source:d.Source,Reasons:['missing client items: '+missing.join(',')]});return false;});
 let triggerIds=new Set(definitions.flatMap(d=>d.Triggers.map(t=>t.Id)));
 definitions=definitions.filter(d=>{if(![...d.BeginActions,...d.ResultActions].some(a=>a.Name==='AddTrigger'&&!triggerIds.has(+a.Args[0])))return true;unsupported.push({Id:d.Id,Name:d.Name,Source:d.Source,Reasons:['trigger definition missing or unsupported']});return false;});
 let duplicateIds=new Set(definitions.filter((d,i)=>definitions.findIndex(x=>x.Id===d.Id)!==i).map(d=>d.Id));definitions=definitions.filter(d=>{if(!duplicateIds.has(d.Id))return true;unsupported.push({Id:d.Id,Source:d.Source,Reasons:['duplicate definition id']});return false;});
 const conversionSource=fs.readFileSync(path.join(sourceDir,'TemplateSDK.lua'),'utf8');
 const professionPairs=[...conversionSource.matchAll(/AddPfTable\(\s*(\d+)\s*,\s*(\d+)\s*\)/g)].map(m=>({From:+m[1],To:+m[2]}));
 const racePairs=[...conversionSource.matchAll(/AddCatTable\(\s*(\d+)\s*,\s*(\d+)\s*\)/g)].map(m=>({From:+m[1],To:+m[2]}));
 const monsters=fs.readFileSync(path.join(resource,'CharacterInfo.txt'),'utf8').split(/\r?\n/).map(l=>l.split('\t')).filter(r=>/^\d+$/.test(r[0])).map(r=>({Id:+r[0],Name:r[1],Level:+r[60]}));
 return {Version:1,Monsters:monsters,ProfessionPairs:professionPairs,RacePairs:racePairs,Total:definitions.length+unsupported.length,Imported:definitions.length,SourceHashes:hashes,Definitions:definitions,Unsupported:unsupported};
}
function archiveSource(archive) {
 const cp=require('node:child_process');const tar=path.join(process.env.SystemRoot||'C:\\Windows','System32','tar.exe');
 const list=cp.execFileSync(tar,['-tf',archive],{encoding:'utf8',maxBuffer:32*1024*1024}).split(/\r?\n/);
 const entries=list.filter(x=>/\/server\/GameServer\/resource\/(?:script\/MisScript\/(?:MissionScript\d+|NpcScript\d+|TemplateSDK)\.lua|script\/MisSdk\/MissionSdk\.lua|CharacterInfo\.txt|[a-z0-9_]+\/[a-z0-9_]+npc\.txt)$/.test(x));
 if(!entries.length)throw Error('Original archive quest sources not found.');
 const cache=path.join(__dirname,'OriginalQuestSource');fs.mkdirSync(cache,{recursive:true});
 cp.execFileSync(tar,['-xf',archive,'-C',cache,...entries],{maxBuffer:16*1024*1024});
 const first=entries.find(x=>/\/MissionScript01\.lua$/.test(x));return path.dirname(path.join(cache,...first.split('/')));
}
if(require.main===module){
 let source=process.argv[2];let archive='E:\\NEW SV\\File 2.rar';if(!source)source=archiveSource(archive);
 let catalog=importCatalog(source);
 let reference='E:\\ToP Server,client,Db,tools\\ToP Server,client,Db,tools\\GameServer\\resource\\script\\MisScript';
 catalog.PrimaryImported=catalog.Imported;catalog.Reconciliations=[];
 if(fs.existsSync(reference)) {
  let alternate=importCatalog(reference);
  for(let failed of catalog.Unsupported.filter(u=>u.Reasons.some(r=>r.startsWith('missing client items:')))) {
   let d=alternate.Definitions.find(d=>d.Id===failed.Id&&d.ResultActions.some(a=>a.Name==='SetProfession'));
   if(!d)continue;
   catalog.Reconciliations.push({...failed,ReplacementSource:reference,ReplacementHash:alternate.SourceHashes[d.Source]});
   d.Source='OriginalReference/'+d.Source;catalog.Definitions.push(d);
  }
  let replaced=new Set(catalog.Reconciliations.map(r=>r.Id));catalog.Unsupported=catalog.Unsupported.filter(u=>!replaced.has(u.Id));catalog.Imported=catalog.Definitions.length;
 }
 const dest=path.join(__dirname,'..','Assets','Resources','OriginalQuests.json');fs.writeFileSync(dest,JSON.stringify(catalog,null,2)+'\n');
 fs.writeFileSync(path.join(__dirname,'original-quest-coverage.json'),JSON.stringify({Total:catalog.Total,Imported:catalog.Imported,PrimaryImported:catalog.PrimaryImported,Source:source,Archive:process.argv[2]?undefined:archive,SourceHashes:catalog.SourceHashes,Reconciliations:catalog.Reconciliations,Unsupported:catalog.Unsupported},null,2)+'\n');
 console.log(catalog.Imported+'/'+catalog.Total+' definitions supported ('+catalog.PrimaryImported+' exact NEW SV; '+catalog.Reconciliations.length+' original-reference class substitutions); '+catalog.Unsupported.length+' unsupported.');
}
module.exports={cleanLua,calls,importCatalog,archiveSource};
