const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const { randomUUID } = require('node:crypto');
const { DatabaseSync } = require('node:sqlite');
const { test } = require('node:test');
const root = path.resolve(__dirname, '..', '..');
const ar = createRequire(path.join(root, 'API', 'package.json'));
const cr = createRequire(path.join(root, 'Cloudflare', 'package.json'));
const secret = 'social-fixture-not-a-real-credential';
const state = () => ({ Version: 1, Fairies: [], Offers: [], StallName: '' });
const source = f => fs.readFileSync(path.join(root, f), 'utf8');
function pure(f) {
    const s=source(f),a=s.indexOf('const gameplayStateValid'),b=s.indexOf('const int =',a);
    const ctx=vm.createContext({});
    vm.runInContext(s.slice(a,b)+';globalThis.rules={gameplayStateValid,stallSale,validStallRequest};',ctx);
    return ctx.rules;
}
test('Both APIs validate gameplay state and derive exactly the same atomic stall transfer',()=>{
    const one=pure('API/game.js'),two=pure('Cloudflare/functions/api/[[path]].js');
    for (const rules of [one,two]) {
        assert.equal(rules.gameplayStateValid(state()),true);
        assert.equal(rules.gameplayStateValid({...state(),Fairies:[{ItemKey:'f',Growth:0,Stamina:32001,Strength:0,Agility:0,Accuracy:0,Constitution:0,Spirit:0}]}),false);
        assert.equal(rules.gameplayStateValid({...state(),Offers:[{ItemKey:'x',ItemId:1780,Quantity:2,Price:1000000000}]}),false);
        assert.equal(rules.validStallRequest({operationId:randomUUID(),sellerId:2,buyerRevision:0,sellerRevision:0,quantity:1,buyerSlot:0,itemKey:'x'}),true);
        assert.equal(rules.validStallRequest({operationId:'bad',sellerId:2}),false);
        const sale=rules.stallSale({operationId:'key',buyerRevision:0,sellerRevision:0,quantity:1,buyerSlot:0,itemKey:'x'},{id:1,save_revision:0,gold:100},{id:2,save_revision:0,gold:50},{unique_item_id:'x',quantity:2,is_equipped:0,is_locked:0,owner_character_id:null},{...state(),StallName:'Shop',Offers:[{ItemKey:'x',Quantity:2,Price:20}]},false);
        assert.equal(sale.response.buyerGold,80); assert.equal(sale.response.sellerGold,70); assert.equal(sale.state.Offers[0].Quantity,1);
        assert.equal(sale.moved.unique_item_id,'key');
    }
});
function d1Fixture() {
    const db=new DatabaseSync(':memory:');
    for (const file of ['0001_init.sql','0003_quest_progress.sql','0004_character_save_transactions.sql','0006_boat_ownership.sql','0007_inventory_fusion_item_id.sql','0008_arena_medal_attributes.sql','0009_character_bank_storage.sql','0010_original_quest_state.sql','0011_gameplay_social_state.sql']) db.exec(source(path.join('Cloudflare','migrations',file)));
    const token=ar('jsonwebtoken').sign({sub:9},secret,{expiresIn:'1h'});
    db.prepare('INSERT INTO accounts(id,username,email,password_hash,session_token,session_expires) VALUES(9,?,?,?,?,?)').run('social_fixture','fixture@example.invalid','unused',token,Math.floor(Date.now()/1000)+3600);
    db.prepare('INSERT INTO accounts(id,username,email,password_hash) VALUES(10,?,?,?)').run('seller_fixture','seller@example.invalid','unused');
    const adapter={ failInsert: false, prepare(sql) { return { bind(...params) { return { sql,params,async first(){return db.prepare(sql).get(...params)||null;},async all(){return {results:db.prepare(sql).all(...params)};} }; } }; }, async batch(stmts) { db.exec('BEGIN'); try { const out=[]; for(const st of stmts){ if(this.failInsert && st.sql.startsWith('INSERT INTO inventory')){this.failInsert=false;throw Error('injected inventory failure');}out.push(db.prepare(st.sql).run(...st.params)); }db.exec('COMMIT');return out;}catch(e){db.exec('ROLLBACK');throw e;} } };
    const js=source(path.join('Cloudflare','functions','api','[[path]].js')).replace(/^import .*;\r?$/gm,'').replace('export const onRequest','const onRequest');
    const context=vm.createContext({Hono:cr('hono').Hono,handle:()=>{},ITEMS:{},console:{error(...args){ console.error("D1 fixture error:", args.map(String).join(" ").slice(0,240).replace(/\n/g," ")); }},TextEncoder,TextDecoder,crypto:globalThis.crypto,btoa,atob,Date,Response,Request,URL});
    vm.runInContext(js+';globalThis.fixture=app;',context);
    return {db,adapter,async call(route,b,method='POST'){const req=new Request('https://fixture.invalid/api/game/'+route,{method,headers:{Authorization:'Bearer '+token,'Content-Type':'application/json'},body:method==='GET'?undefined:JSON.stringify(b)});return (await context.fixture.fetch(req,{DB:adapter,JWT_SECRET:secret})).json();}};
}
function creationBody() {return {OperationId:randomUUID(),QuestId:0,Character:{Id:100,Name:'Buyer',SaveRevision:0,Level:1,Job:0,Exp:0,Gold:0,GameplayStateVersion:1,Gameplay:state(),GuildCreateName:'OriginalCrew'},Inventory:[],Skills:[]};}
test('D1 original guild creation atomically consumes oath/gold and idempotently creates membership',async()=>{
    const f=d1Fixture();try{
        f.db.exec("INSERT INTO characters(id,account_id,slot_index,name,gold) VALUES(100,9,0,'Buyer',100000)");
        f.db.exec("INSERT INTO inventory(unique_item_id,character_id,slot_index,item_id,quantity) VALUES('oath',100,0,1780,1)");
        assert.equal((await f.call('capabilities',null,'GET')).gameplayStateVersion,1);
        const b=creationBody(),first=await f.call('characters/100',b,'PUT');
        assert.equal(first.success,true,JSON.stringify(first));assert.equal(first.gameplayStateVersion,1);
        assert.equal((await f.call('characters/100',b,'PUT')).success,true);
        assert.equal(f.db.prepare('SELECT gold FROM characters WHERE id=100').get().gold,0);
        assert.equal(f.db.prepare('SELECT COUNT(*) n FROM inventory WHERE character_id=100').get().n,0);
        assert.equal(f.db.prepare('SELECT COUNT(*) n FROM guild_members WHERE character_id=100').get().n,1);
        assert.equal((await f.call('guild/100/create',{name:'WrongPath'})).error,'ATOMIC_SAVE_REQUIRED');
        f.db.exec("INSERT INTO characters(id,account_id,slot_index,name,gold) VALUES(200,10,0,'Candidate',0)");
        assert.equal((await f.call('guild/100/invite',{targetName:'Candidate'})).success,true);
        assert.equal((await f.call('guild/100/rank',{targetName:'Candidate',officer:true})).success,true);
        assert.equal(f.db.prepare('SELECT rank_name FROM guild_members WHERE character_id=200').get().rank_name,'Oficial');
        assert.equal((await f.call('guild/100/leave',{})).success,true);
        assert.equal(f.db.prepare('SELECT rank_name FROM guild_members WHERE character_id=200').get().rank_name,'Lider');
        assert.equal(f.db.prepare('SELECT leader_character_id FROM guilds').get().leader_character_id,200);
    }finally{f.db.close();}
});
const stallState=()=>({...state(),StallName:'OriginalShop',Offers:[{ItemKey:'item',ItemId:1780,Quantity:3,Price:50}]});
const request=()=>({operationId:randomUUID(),sellerId:200,buyerRevision:0,sellerRevision:0,quantity:1,buyerSlot:0,itemKey:'item'});
async function stallLifecycle(call,read,fail) {
    const b=request(),first=await call(b);assert.equal(first.success,true,JSON.stringify(first));
    assert.equal(first.buyerGold,950);assert.equal(first.sellerGold,150);
    assert.deepEqual(await call(b),first);
    assert.equal((await call({...b,quantity:2})).error,'OPERATION_MISMATCH');
    const current={...request(),buyerRevision:1,sellerRevision:1,buyerSlot:1};
    if(fail){fail();assert.equal((await call(current)).success,false);assert.equal((await read('buyer')).gold,950);assert.equal((await read('item')).quantity,2);}
    const results=await Promise.all([call(current),call({...current,operationId:randomUUID()})]);
    assert.equal(results.filter(r=>r.success).length,1);
    assert.equal((await read('buyer')).gold,900);assert.equal((await read('seller')).gold,200);assert.equal((await read('item')).quantity,1);
    assert.equal((await call({...request(),buyerRevision:2,sellerRevision:2,buyerSlot:0})).success,false,'occupied slot must fail');
}
test('D1 stalls atomically transfer modified items/gold; replay, concurrency and failure rollback',async()=>{
    const f=d1Fixture();try{
        f.db.exec("INSERT INTO characters(id,account_id,slot_index,name,gold) VALUES(100,9,0,'Buyer',1000),(200,10,0,'Seller',100)");
        f.db.prepare('UPDATE characters SET gameplay_state_json=? WHERE id=200').run(JSON.stringify(stallState()));
        f.db.exec("INSERT INTO inventory(unique_item_id,character_id,slot_index,item_id,quantity,durability,fusion_item_id,refine_level,gem_slot_1) VALUES('item',200,0,1780,3,777,2530,4,885)");
        await stallLifecycle(b=>f.call('stalls/100/buy',b),what=>f.db.prepare(what==='item'?"SELECT * FROM inventory WHERE unique_item_id='item'":'SELECT * FROM characters WHERE id='+ (what==='buyer'?100:200)).get(),()=>{f.adapter.failInsert=true;});
        const moved=f.db.prepare('SELECT * FROM inventory WHERE character_id=100 ORDER BY slot_index LIMIT 1').get();
        assert.equal(moved.durability,777);assert.equal(moved.fusion_item_id,2530);assert.equal(moved.refine_level,4);assert.equal(moved.gem_slot_1,885);
    }finally{f.db.close();}
});
test('MariaDB stalls and guild creation use only connection-scoped temporary tables',{skip:process.env.TOP_TEST_MARIADB!=='1'},async()=>{
    ar('dotenv').config({path:[path.join(root,'API','.env.local'),path.join(root,'API','.env')],quiet:true});
    const host=process.env.DB_HOST || '127.0.0.1';assert.ok(['127.0.0.1','localhost','::1'].includes(host));
    const db=await ar('mysql2/promise').createConnection({host,port:Number(process.env.DB_PORT||3306),user:process.env.DB_USER||'root',password:process.env.DB_PASS,database:process.env.DB_NAME||'top_unity'});
    try {
        for(const table of ['characters','inventory','guilds','guild_members']) { const [ddl]=await db.execute('SHOW CREATE TABLE '+table); await db.query(ddl[0]['Create Table'].replace('CREATE TABLE','CREATE TEMPORARY TABLE').replace(/^\s*CONSTRAINT[^\n]+\n/gm,'').replace(/,\n\)/g,'\n)')); }
        await db.query('ALTER TABLE characters ADD COLUMN IF NOT EXISTS gameplay_state_json LONGTEXT');
        await db.query('ALTER TABLE characters ADD COLUMN IF NOT EXISTS save_revision BIGINT NOT NULL DEFAULT 0');
        for (const file of ['0007_inventory_fusion_item_id.sql','0008_arena_medal_attributes.sql']) for(const st of source(path.join('API','migrations',file)).split(';').filter(s=>s.trim()))await db.query(st);
        await db.execute('CREATE TEMPORARY TABLE character_save_receipts(character_id BIGINT,operation_id VARCHAR(36),payload_hash CHAR(64),expected_revision BIGINT,quest_id INT,PRIMARY KEY(character_id,operation_id))');
        await db.execute('CREATE TEMPORARY TABLE stall_purchase_receipts(buyer_id BIGINT,operation_id VARCHAR(36),payload_hash CHAR(64),response_json LONGTEXT,PRIMARY KEY(buyer_id,operation_id))');
        await db.execute("INSERT INTO characters(id,account_id,slot_index,name,gold) VALUES(100,9,0,'Buyer',1000),(200,10,0,'Seller',100)");
        await db.execute('UPDATE characters SET gameplay_state_json=? WHERE id=200',[JSON.stringify(stallState())]);
        await db.execute("INSERT INTO inventory(unique_item_id,character_id,slot_index,item_id,quantity,durability,fusion_item_id,refine_level,gem_slot_1) VALUES('item',200,0,1780,3,777,2530,4,885)");
        const routes=new Map(),app={use(){},get(){},post(route,handler){routes.set(route,handler);},put(route,handler){routes.set(route,handler);},delete(){}};
        const adapter={execute:(...a)=>db.execute(...a),async getConnection(){return {execute:(...a)=>db.execute(...a),beginTransaction:()=>db.beginTransaction(),commit:()=>db.commit(),rollback:()=>db.rollback(),release(){}};}};
        ar(path.join(root,'API','game.js'))(app,adapter);
        let tail=Promise.resolve();const call=(b,route='/api/game/stalls/:charId/buy')=>{const work=tail.then(async()=>{let out;await routes.get(route)({params:{charId:100,id:100},body:b,user:{id:9}},{json(v){out=v;return this;},status(){return this;}});return out;});tail=work.catch(()=>{});return work;};
        await stallLifecycle(call,async what=>(await db.execute(what==='item'?"SELECT * FROM inventory WHERE unique_item_id='item'":'SELECT * FROM characters WHERE id='+ (what==='buyer'?100:200)))[0][0]);
        await db.execute("INSERT INTO characters(id,account_id,slot_index,name,gold) VALUES(101,9,1,'Founder',100000)");
        await db.execute("INSERT INTO inventory(unique_item_id,character_id,slot_index,item_id,quantity) VALUES('oath',101,0,1780,1)");
        const body=creationBody();body.Character.Id=101;body.Character.Name='Founder';
        let result;await routes.get('/api/game/characters/:id')({params:{id:101},body,user:{id:9}},{json(v){result=v;return this;},status(){return this;}});
        assert.equal(result.success,true,JSON.stringify(result));
        assert.equal(Number((await db.execute('SELECT gold FROM characters WHERE id=101'))[0][0].gold),0);
        assert.equal((await db.execute('SELECT * FROM guild_members WHERE character_id=101'))[0].length,1);
    }finally{await db.end();}
});

test('Original skill numeric function import preserves Lua costs and level cooldowns',()=>{
    const catalog=JSON.parse(source('Assets/Resources/PKO/OriginalSkillParameters.json'));
    assert.equal(catalog.Entries.length,411);
    assert.equal(catalog.Entries.find(e=>e.Id===81).Costs[1],20);
    assert.equal(catalog.Entries.find(e=>e.Id===81).Cooldowns[1],5000);
    assert.equal(catalog.Entries.find(e=>e.Id===17).Cooldowns[10],5000);
});
