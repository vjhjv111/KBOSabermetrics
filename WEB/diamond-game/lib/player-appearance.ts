export type PlayerAppearance={team:string;name:string;number?:string;jersey:string;cap:string;accent:string;letter:string;wordmark:string};
const palettes:Record<string,[string,string,string,string,string]>={
 HH:["#f15c22","#20252c","#fff1d8","#ffffff","HANWHA"],
 HT:["#bf2538","#142139","#eeeeed","#ffffff","KIA"],
 KT:["#252931","#161a20","#ec293c","#ffffff","KT"],
 LG:["#f1eee8","#1d202a","#c52655","#a61847","LG"],
 LT:["#b72740","#172847","#dce8f1","#ffffff","LOTTE"],
 NC:["#224b79","#21364f","#c4a26b","#ffffff","NC"],
 OB:["#eff0e9","#16233b","#d42c44","#192b47","DOOSAN"],
 SK:["#bb2639","#b82437","#edc9a3","#ffffff","SSG"],
 SS:["#2671bf","#1b5da5","#e1e9f1","#ffffff","SAMSUNG"],
 WO:["#71263c","#5a2034","#e5c5cc","#ffffff","KIWOOM"]
};
const aliases:Record<string,string>={한화:"HH",KIA:"HT",KT:"KT",LG:"LG",롯데:"LT",NC:"NC",두산:"OB",SSG:"SK",SK:"SK","SSG/SK":"SK",삼성:"SS",키움:"WO",히어로즈:"WO"};
export function playerAppearance(team:string,name:string,number?:unknown):PlayerAppearance{
 const code=aliases[team.toUpperCase()]??team.toUpperCase();
 const [jersey,cap,accent,letter,wordmark]=palettes[code]??["#617383","#24384a","#d2d9de","#ffffff",team||"BASEBALL"];
 // A player ID or a generated number must never be presented as a jersey number.
 const confirmedNumber=(typeof number==="string"||typeof number==="number")&&/^\d{1,3}$/.test(String(number).trim())?String(number).trim():undefined;
 return {team:code,name,number:confirmedNumber,jersey,cap,accent,letter,wordmark};
}
