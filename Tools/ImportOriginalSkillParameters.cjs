const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..');
const original=process.argv[2] || 'E:\\PrivateTop\\meu_servidor\\server\\GameServer\\resource\\script\\calculate\\skilleffect.lua';
const lua=fs.readFileSync(original,'latin1');
const rows=fs.readFileSync(path.join(root,'Assets','Resources','PKO','skillinfo.txt'),'latin1').split(/\r?\n/).filter(s=>/^\d+\t/.test(s)).map(s=>s.split('\t'));
function resolve(source) {
    if (!source || source==='0') return '0';
    if (!/^[A-Za-z_][A-Za-z_0-9]*$/.test(source)) return source.replace(/sklv\(0\)/g,'level');
    const at=lua.indexOf('function '+source+'(sklv)'); if(at<0)return null;
    const next=lua.indexOf('\nfunction ',at+10),block=lua.slice(at,next<0?lua.length:next);
    if (/\bif\b|\bwhile\b|\bfor\b/.test(block.replace(/--[^\n]*/g,''))) return null;
    const ret=block.match(/return\s+([^\n]+)/);if(!ret)return null;
    let expr=ret[1].trim();
    const vars=[...block.matchAll(/local\s+(\w+)\s*=\s*([^\n]+)/g)];
    for(let i=vars.length-1;i>=0;i--)expr=expr.replace(new RegExp('\\b'+vars[i][1]+'\\b','g'),'('+vars[i][2].trim()+')');
    return expr.replace(/\bsklv\b/g,'level');
}
function values(expr) {
    if(expr===null)return null;
    expr=expr.replace(/--.*$/,'').replace(/math\./g,'Math.').replace(/Math\.pow/g,'Math.pow');
    if(!/^[0-9a-zA-Z_.+*\-/(),\s]+$/.test(expr) || /[A-Za-z_]\w*/g.test(expr.replace(/Math\.(?:floor|ceil|min|max|pow)|level/g,'')))return null;
    try {const evaluate=new Function('level','return '+expr);const result=Array.from({length:101},(_,level)=>Math.max(0,Math.round(evaluate(Math.max(1,level)))));return result.every(n=>Number.isSafeInteger(n)&&n>=0)?result:null;}catch{return null;}
}
const entries=rows.map(a=>({Id:Number(a[0]),Costs:values(resolve(a[24]&&a[24]!=='0'?a[24]:((a[33]||'').match(/^sp=sp\(0\)-(.+)$/)||[])[1] || '0')),Cooldowns:values(resolve(a[44]))}));
fs.writeFileSync(path.join(root,'Assets','Resources','PKO','OriginalSkillParameters.json'),JSON.stringify({Entries:entries}));
console.log('Imported original skill parameter rows='+entries.length+', costs='+entries.filter(e=>e.Costs).length+', cooldowns='+entries.filter(e=>e.Cooldowns).length);
