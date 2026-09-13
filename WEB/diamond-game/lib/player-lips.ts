// Lip planes and a conforming mouth ribbon share the current facial surface.
import * as THREE from 'three';
import {gauss} from './player-geometry';

export type LipSurface=(x:number,y:number)=>number;
export function createLipGeometries(faceZ:LipSurface){
 const columns=20,rows=8,positions:number[]=[],colors:number[]=[],uv:number[]=[],indices:number[]=[];
 const grid:number[][]=Array.from({length:rows+1},()=>[]);
 const creaseRows:THREE.Vector3[][]=Array.from({length:2},()=>[]);
 for(let col=0;col<=columns;col++){
  const t=col/columns*2-1,x=t*.024,arch=Math.max(0,1-t*t);
  // Exact original outer contour and per-column lip tint.
  const crease=-.026+gauss(Math.abs(t),.3,.22)*.0008;
  const bottom=crease-.0042*arch,top=crease+(.0025+gauss(Math.abs(t),.28,.23)*.0015)*arch;
  const halfLine=.00022*Math.pow(arch,.65),lower=crease-halfLine,upper=crease+halfLine;
  const heights=[bottom,THREE.MathUtils.lerp(bottom,lower,.44),THREE.MathUtils.lerp(bottom,lower,.76),lower,crease,upper,THREE.MathUtils.lerp(upper,top,.24),THREE.MathUtils.lerp(upper,top,.58),top];
  // Two shallow rounded ridges, separated by a narrow valley. The valley's
  // three rows are collinear, avoiding a suspended stripe over a V-shaped gap.
  const relief=[0,.00155,.00095,.00025,.00025,.00025,.00085,.00125,0];
  const depths=heights.map((y,row)=>faceZ(x,y)+.0004+relief[row]*arch);
  depths[4]=(depths[3]+depths[5])*.5;
  for(let row=0;row<=rows;row++){
   // The contour collapses at the mouth corners. Use a true fan there instead
   // of zero-area triangles and zero normals from nine duplicated vertices.
   const corner=col===0||col===columns;
   if(corner&&row>0){grid[row][col]=grid[0][col];continue;}
   grid[row][col]=positions.length/3;
   positions.push(x,heights[row],depths[row]);
   colors.push(1,.80+Math.abs(t)*.09,.77+Math.abs(t)*.10);
   uv.push(col/columns,arch?(heights[row]-bottom)/(top-bottom):.5);
  }
  creaseRows[0].push(new THREE.Vector3(x,lower,depths[3]));
  creaseRows[1].push(new THREE.Vector3(x,upper,depths[5]));
 }
 const tri=(a:number,b:number,c:number)=>{if(a!==b&&b!==c&&a!==c)indices.push(a,b,c);};
 for(let row=0;row<rows;row++)for(let col=0;col<columns;col++){
  const a=grid[row][col],b=grid[row][col+1],c=grid[row+1][col+1],d=grid[row+1][col];
  // Mirrored diagonals preserve left/right normals as well as vertex shape.
  if(col<columns/2){tri(a,b,d);tri(b,c,d);}else{tri(a,b,c);tri(a,c,d);}
 }
 const lips=new THREE.BufferGeometry();lips.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));
 lips.setAttribute('color',new THREE.Float32BufferAttribute(colors,3));lips.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));lips.setIndex(indices);lips.computeVertexNormals();

 const ribbonPositions:number[]=[],ribbonUV:number[]=[],ribbonIndices:number[]=[],offset=.00007;
 for(let row=0;row<2;row++)for(let col=0;col<=columns;col++){
  const point=creaseRows[row][col].clone();
  // Preserve the original 46mm line span, inside the 48mm lip outline. These
  // endpoints lie on the rendered valley edge rather than an analytic resample.
  if(col===0)point.lerp(creaseRows[row][1],(.024-.023)/(.048/columns));
  if(col===columns)point.lerp(creaseRows[row][columns-1],(.024-.023)/(.048/columns));
  ribbonPositions.push(point.x,point.y,point.z+offset);ribbonUV.push(col/columns,row);
 }
 for(let col=0;col<columns;col++){
  const a=col,b=col+1,c=col+columns+2,d=col+columns+1;
  if(col<columns/2)ribbonIndices.push(a,b,d,b,c,d);else ribbonIndices.push(a,b,c,a,c,d);
 }
 const mouth=new THREE.BufferGeometry();mouth.setAttribute('position',new THREE.Float32BufferAttribute(ribbonPositions,3));
 mouth.setAttribute('uv',new THREE.Float32BufferAttribute(ribbonUV,2));mouth.setIndex(ribbonIndices);mouth.computeVertexNormals();
 return {lips,mouth};
}
