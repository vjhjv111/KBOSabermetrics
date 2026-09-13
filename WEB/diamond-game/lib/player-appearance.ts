export type PlayerBodyType="lean"|"athletic"|"power";
export type PlayerHairStyle="short"|"buzz"|"flow"|"bald";
/** Optional overrides describe the custom avatar, never inferred real-player identity. */
export type PlayerCustomization={skinTone?:string;bodyType?:PlayerBodyType;heightCm?:number;hairStyle?:PlayerHairStyle;hairColor?:string;gloveColor?:string;cleatColor?:string;batColor?:string;equipmentColor?:string;jerseyNumber?:string};
export type PlayerAppearance={team:string;name:string;number?:string;jersey:string;cap:string;accent:string;letter:string;wordmark:string}&PlayerCustomization;
export const DEFAULT_PLAYER_CUSTOMIZATION:Required<PlayerCustomization>={skinTone:"#c89675",bodyType:"athletic",heightCm:185,hairStyle:"short",hairColor:"#242421",gloveColor:"#915b31",cleatColor:"#172027",batColor:"#bc8b52",equipmentColor:"#27313b",jerseyNumber:""};
const hexColor=(value:unknown,fallback:string)=>typeof value==="string"&&/^#[0-9a-f]{6}$/i.test(value)?value:fallback;
export function normalizePlayerCustomization(appearance:PlayerCustomization={}):Required<PlayerCustomization>{
 const defaults=DEFAULT_PLAYER_CUSTOMIZATION;
 return {skinTone:hexColor(appearance.skinTone,defaults.skinTone),bodyType:appearance.bodyType==="lean"||appearance.bodyType==="power"?appearance.bodyType:"athletic",heightCm:Number.isFinite(appearance.heightCm)?Math.max(155,Math.min(215,appearance.heightCm!)):185,hairStyle:["short","buzz","flow","bald"].includes(appearance.hairStyle??"")?appearance.hairStyle!:"short",hairColor:hexColor(appearance.hairColor,defaults.hairColor),gloveColor:hexColor(appearance.gloveColor,defaults.gloveColor),cleatColor:hexColor(appearance.cleatColor,defaults.cleatColor),batColor:hexColor(appearance.batColor,defaults.batColor),equipmentColor:hexColor(appearance.equipmentColor,defaults.equipmentColor),jerseyNumber:typeof appearance.jerseyNumber==="string"&&/^\d{1,2}$/.test(appearance.jerseyNumber)?appearance.jerseyNumber:""};
}
/** Height is applied by the scene to the whole rig, keeping IK proportions intact. */
export function playerDimensions(appearance:PlayerCustomization={}){
 const look=normalizePlayerCustomization(appearance),width=look.bodyType==="lean"?.91:look.bodyType==="power"?1.13:1;
 return {heightScale:look.heightCm/185,widthScale:width,depthScale:look.bodyType==="power"?1.15:look.bodyType==="lean"?.94:1};
}
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
export function playerAppearance(team:string,name:string,number?:unknown,customization:PlayerCustomization={}):PlayerAppearance{
 const code=aliases[team.toUpperCase()]??team.toUpperCase();
 const [jersey,cap,accent,letter,wordmark]=palettes[code]??["#617383","#24384a","#d2d9de","#ffffff",team||"BASEBALL"];
 // A player ID or a generated number must never be presented as a jersey number.
 const confirmedNumber=(typeof number==="string"||typeof number==="number")&&/^\d{1,3}$/.test(String(number).trim())?String(number).trim():undefined;
 return {team:code,name,number:normalizePlayerCustomization(customization).jerseyNumber||confirmedNumber,jersey,cap,accent,letter,wordmark,...customization};
}
