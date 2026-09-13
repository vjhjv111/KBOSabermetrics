import * as THREE from 'three';
import {add,joined,loft,tube,restoreSeamNormals} from './player-geometry';

/** Closed leather bowl; the first flat ring keeps the ball tangent at the floor. */
function bowl(width:number,height:number,floor:number,rise:number,thickness:number,rows=7,around=28){
 const positions:number[]=[],uv:number[]=[],indices:number[]=[],pairs:Array<[number,number]>=[];
 const sideCount=1+rows*(around+1);
 for(let back=0;back<2;back++){
  positions.push(0,-.02,floor-back*thickness);uv.push(.5,.5);
  for(let row=1;row<=rows;row++){
   const r=row/rows,start=positions.length/3;pairs.push([start,start+around]);
   for(let col=0;col<=around;col++){
    const angle=col/around*Math.PI*2,x=Math.sin(angle)*width*r,y=Math.cos(angle)*height*r-.02;
    const relief=rise*Math.max(0,r*r-.035)/.965;
    positions.push(x,y,floor+relief-back*thickness);uv.push(x/(width*2)+.5,(y+.02)/(height*2)+.5);
   }
  }
 }
 for(let back=0;back<2;back++){
  const offset=back*sideCount;
  for(let col=0;col<around;col++)indices.push(...(back?[offset,offset+1+col,offset+2+col]:[offset,offset+2+col,offset+1+col]));
  for(let row=0;row<rows-1;row++)for(let col=0;col<around;col++){
   const a=offset+1+row*(around+1)+col,b=a+1,c=b+around+1,d=a+around+1;
   indices.push(...(back?[a,d,b,b,d,c]:[a,b,d,b,c,d]));
  }
 }
 const edge=1+(rows-1)*(around+1);
 for(let col=0;col<around;col++){const a=edge+col,b=a+1;indices.push(a,b,a+sideCount,b,b+sideCount,a+sideCount);}
 const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();geometry.userData.normalSeamPairs=pairs;return restoreSeamNormals(geometry);
}

/** One padded crescent, thumb lobe and heel: no exposed finger tubes. */
function roundedPadding(){
 const positions:number[]=[],uv:number[]=[],indices:number[]=[],around=32,cross=8;
 for(let ring=0;ring<around;ring++){
  const a=ring/around*Math.PI*2,s=Math.sin(a),c=Math.cos(a),thumb=Math.exp(-(((a-1.22)/.52)**2)),heel=Math.max(0,-c);
  const width=.131+.01*thumb,radial=.025+.006*thumb-.007*heel,depth=.025+.007*thumb-.004*heel;
  for(let col=0;col<cross;col++){
   const t=col/cross*Math.PI*2,r=Math.cos(t)*radial,x=s*(width+r),y=c*(.135+r)-.02,z=-.003+Math.sin(t)*depth;
   positions.push(x,y,z);uv.push(x/.34+.5,(y+.02)/.34+.5);
   const next=(ring+1)%around,other=(col+1)%cross,a0=ring*cross+col,b=next*cross+col,c0=next*cross+other,d=ring*cross+other;
   indices.push(a0,d,b,b,d,c0);
  }
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
}
function leatherWeb(){
 const outline=[[-.006,.112],[.039,.108],[.081,.082],[.096,.104],[.055,.138],[.006,.145]],n=outline.length,positions:number[]=[],uv:number[]=[],indices:number[]=[];
 for(const z of [.003,-.007])for(const [x,y] of outline){positions.push(x,y,z);uv.push(x*5,y*5);}
 // This stitched web spans only the upper/thumb cleft, leaving the bowl open.
 for(let i=1;i<n-1;i++)indices.push(0,i,i+1,n,n+i+1,n+i);
 for(let i=0;i<n;i++){const j=(i+1)%n;indices.push(i,n+i,j,j,n+i,n+j);}
 const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
}

/** Geometry-only builder. Materials are owned by the existing mitt assembly. */
export function buildCatcherMitt(root:THREE.Object3D,leather:THREE.Material,trim:THREE.Material,pocket:THREE.Material){
 add(bowl(.112,.121,-.052,.043,.007),pocket,root).name='Deep leather mitt pocket';
 const cuff=loft([[-.139,.047,.026,-.035],[-.118,.05,.029,-.036],[-.098,.043,.026,-.037]],16,2);
 add(joined([roundedPadding(),bowl(.135,.138,-.084,.058,.008,5,24),cuff]),leather,root).name='Catcher mitt rounded padding and heel';
 add(leatherWeb(),leather,root).name='Catcher mitt stitched thumb web';
 const laces:number[][][]=[],inner:number[][]=[],back:number[][]=[];
 for(let i=0;i<=32;i++){
  const a=i/32*Math.PI*2;inner.push([Math.sin(a)*.11,Math.cos(a)*.114-.02,.007]);back.push([Math.sin(a)*.146,Math.cos(a)*.15-.02,-.007]);
 }
 laces.push(inner,back);
 for(let i=0;i<16;i++){
  const a=i/16*Math.PI*2,b=a+.065;
  laces.push([[Math.sin(a)*.132,Math.cos(a)*.136-.02,.02],[Math.sin(b)*.147,Math.cos(b)*.15-.02,.012],[Math.sin(b+.025)*.147,Math.cos(b+.025)*.15-.02,-.007]]);
 }
 laces.push([[.01,.127,.012],[.041,.121,.014],[.078,.099,.014]],[[.022,.139,.011],[.058,.129,.012],[.087,.108,.012]],[[.071,.06,.019],[.106,.079,.027],[.128,.063,.026]],[[.071,.06,.019],[.084,.025,.024],[.124,.014,.027]],[[-.042,-.123,-.007],[0,-.13,-.005],[.042,-.123,-.007]]);
 add(joined(laces.map((path,i)=>tube(path,.0019,i<2?28:3))),trim,root).name='Catcher mitt welt and lacing';
 const creases=[[[.047,-.066,-.043],[.021,-.055,-.05],[-.008,-.046,-.052]],[[-.071,-.011,-.034],[-.057,.013,-.041],[-.027,.035,-.042]]];
 add(joined(creases.map(path=>tube(path,.0008,5))),leather,root).name='Catcher mitt pocket folds';
}
