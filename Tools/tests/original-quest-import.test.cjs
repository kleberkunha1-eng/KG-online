const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {cleanLua,calls,importCatalog}=require('../Import-OriginalQuests.cjs');
const catalog=require('../../Assets/Resources/OriginalQuests.json');
test('Lua importer respects comments, escaped strings, tables and balanced calls',()=>{
 const source=cleanLua(`-- DefineMission(99, "comment", 99)\nDefineMission(1, "don't -- delete (this)", 7) -- suffix\nMisBeginCondition(CTypeCheck, {1,3})\n--[[ DefineMission(8,"hidden",8) ]]`);
 const parsed=calls(source);assert.equal(parsed.length,2);assert.deepEqual(parsed[0].args,['1',`"don't -- delete (this)"`,'7']);assert.deepEqual(parsed[1].args,['CTypeCheck','{1,3}']);
});
test('Generated catalog covers original definitions without silently treating native unsupported commands as success',()=>{
 const coverage=require('../original-quest-coverage.json');assert.equal(catalog.Total,1508);assert.equal(catalog.Imported,catalog.Definitions.length);
 assert.equal(catalog.Total,catalog.Imported+catalog.Unsupported.length);assert.ok(catalog.Imported>=900);
 assert.equal(coverage.Unsupported.length,catalog.Unsupported.length);assert.equal(new Set(catalog.Definitions.map(d=>d.Id)).size,catalog.Imported);
 assert.ok(catalog.Unsupported.every(d=>d.Reasons.length>0));assert.ok(catalog.Unsupported.some(d=>d.Reasons.includes('random mission generator')));
 assert.ok(catalog.Definitions.some(d=>d.ResultActions.some(a=>a.Name==='SetProfession')));
 assert.equal(catalog.Monsters.find(m=>m.Id===188).Level,3);
 assert.equal(catalog.Monsters.find(m=>m.Id===1551).Level,1);
 assert.ok(catalog.ProfessionPairs.some(p=>p.From===1&&p.To===9));assert.ok(catalog.RacePairs.some(p=>p.From===2&&p.To===8));
 const triggers=new Set(catalog.Definitions.flatMap(d=>d.Triggers.map(t=>t.Id)));
 assert.ok(catalog.Definitions.every(d=>[...d.BeginActions,...d.ResultActions].filter(a=>a.Name==='AddTrigger').every(a=>triggers.has(+a.Args[0]))));
});
test('Original Senna delivery chain uses native mission/record IDs distinct from display definitions',()=>{
 const find=id=>catalog.Definitions.find(d=>d.Id===id);assert.equal(find(702).MissionId,701);assert.equal(find(703).MissionId,701);
 assert.equal(find(703).CompletionOnly,true);assert.ok(find(703).NpcNames.includes('Blacksmith - Goldie'));
 assert.deepEqual(find(703).ResultActions.map(a=>a.Name),['TakeItem','SetFlag']);
 assert.ok(find(702).ResultActions.some(a=>a.Name==='SetRecord'&&a.Args[0]==='701'));
 assert.ok(find(704).BeginConditions.some(a=>a.Name==='HasRecord'&&a.Args[0]==='701'));
 assert.ok(!catalog.Definitions.some(d=>[733,738].includes(d.Id)), 'Commented Ditto bindings remain disabled rather than overwriting legacy quests.');
});
