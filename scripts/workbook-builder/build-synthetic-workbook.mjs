import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { Workbook, SpreadsheetFile, FileBlob } from '@oai/artifact-tool';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const output = path.join(root, 'outputs/synthetic-workbooks');
await fs.mkdir(output, {recursive:true});
if (process.argv[2]?.startsWith('--preview=')) {
 const type=process.argv[2].split('=')[1];
 if(!['DPL','TFA','CRD'].includes(type)) throw Error('Invalid preview sheet');
 const saved=await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(output,'SYNTH-001-FY2026.xlsx')));
 const preview=await saved.render({sheetName:type,range:'A1:J9',scale:1,format:'png'});
 await fs.writeFile(path.join(output,`${type}-preview.png`),new Uint8Array(await preview.arrayBuffer()));
 process.exit(0);
}
const descriptions = {
 DPL: ['Office electricity supply bill', 'Business liability insurance premium', 'Accountant fees for annual accounts', 'Monthly office premises rental', 'Rail tickets and hotel for business travel'],
 TFA: ['Laptop computers for office employees', 'Ergonomic office desks and chairs', 'Delivery van for distribution operations', 'Industrial production machinery for factory', 'Installed shelving and lighting fittings'],
 CRD: ['Purchase of new server equipment', 'Routine maintenance and cleaning service', 'Combined invoice for new hardware and monthly support', 'General invoice with no supporting breakdown', 'Office building extension construction costs']
};
const headers = ['ClientId','EntityId','Period','RecordId','AccountCode','Description','Amount','Currency','IsSynthetic','SourceReference'];
const workbook=Workbook.create();
for (const [type,bases] of Object.entries(descriptions)) {
 const sheet=workbook.worksheets.add(type);
 const rows=Array.from({length:50},(_,index)=>{
  const branch=1+Math.floor(index/5);
  return ['SYNTH-001','SYNTH-ENTITY-01','FY2026',`${type}-${String(index+1).padStart(3,'0')}`,
   `${type}-${4100+(index%5)*10}`,`${bases[index%5]} for branch ${branch}`,
   Math.round((175+(index+1)*93.27+(type==='TFA'?1500:0))*100)/100,'GBP',true,`SYNTH-INV-${type}-${index+1}`];
 });
 sheet.getRange('A1:J51').values=[headers,...rows];
 sheet.showGridLines=false;
 sheet.getRange('A1:J51').format.font={name:'Arial',size:10,color:'#203047'};
 sheet.getRange('A1:J1').format={fill:'#183B60',font:{name:'Arial',size:10,bold:true,color:'#FFFFFF'},rowHeight:28};
 sheet.getRange('A2:J51').format.rowHeight=22;
 for(const col of ['A','B','C','D','E','H','I','J']) sheet.getRange(`${col}1:${col}51`).format.columnWidth=col==='J'?28:col==='B'?23:18;
 sheet.getRange('F1:F51').format.columnWidth=76;
 sheet.getRange('G1:G51').format.columnWidth=16;
 sheet.getRange('G2:G51').setNumberFormat('#,##0.00');
 sheet.freezePanes.freezeRows(1);
 sheet.getRange('L1').values=[['Synthetic data — 50 unlabeled rows; amounts in GBP']];
 sheet.getRange('L1').format.columnWidth=66;
 sheet.getRange('L1').format.font={name:'Arial',size:10,italic:true,color:'#596579'};
}
workbook.recalculate();
console.log((await workbook.inspect({kind:'sheet',include:'id,name',maxChars:1000})).ndjson);
const xlsx=await SpreadsheetFile.exportXlsx(workbook);
await xlsx.save(path.join(output,'SYNTH-001-FY2026.xlsx'));
for(const type of Object.keys(descriptions)) {
 const sheet=workbook.worksheets.getItem(type);
 if(sheet.getRange('A2:J51').values.length!==50) throw Error('Unexpected row count');
 // Isolate rendering to avoid shared native glyph-cache state between worksheets.
 const preview=spawnSync(process.execPath,[fileURLToPath(import.meta.url),`--preview=${type}`],{stdio:'inherit'});
 if(preview.status!==0) throw Error(`Failed to render ${type}`);
}
console.log('Saved one workbook with 50 DPL, 50 TFA and 50 CRD rows; no category answers in input.');
