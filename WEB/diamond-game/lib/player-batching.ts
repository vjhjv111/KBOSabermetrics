import * as THREE from 'three';

export type PlayerBatchPart={sourceId:string;name:string;vertexStart:number;vertexCount:number;indexStart:number;indexCount:number};
export type PlayerBatchReport={groups:number;removedMeshes:number;disposedGeometries:number};

function attributesKey(geometry:THREE.BufferGeometry){
 return Object.keys(geometry.attributes).sort().map(name=>{
  const attribute=geometry.getAttribute(name) as THREE.BufferAttribute;
  return [name,attribute.name,attribute.itemSize,attribute.normalized,attribute.array.constructor.name,attribute.usage,attribute.gpuType];
 });
}

function eligible(mesh:THREE.Mesh,excluded:Set<THREE.Object3D>){
 const geometry=mesh.geometry;
 if(mesh.constructor!==THREE.Mesh||mesh.type!=='Mesh'||mesh.children.length||!mesh.parent||excluded.has(mesh)||mesh.name==='Custom hair'||Array.isArray(mesh.material)||mesh.material.transparent)return false;
 if(mesh.customDepthMaterial||mesh.customDistanceMaterial||mesh.animations.length||mesh.morphTargetInfluences||mesh.morphTargetDictionary)return false;
 if(mesh.onBeforeRender!==THREE.Object3D.prototype.onBeforeRender||mesh.onAfterRender!==THREE.Object3D.prototype.onAfterRender)return false;
 if(mesh.onBeforeShadow!==THREE.Object3D.prototype.onBeforeShadow||mesh.onAfterShadow!==THREE.Object3D.prototype.onAfterShadow)return false;
 if(Object.keys(mesh.userData).some(key=>key!=='playerBatchParts')||Object.keys(geometry.userData).some(key=>key!=='restShape'&&key!=='normalSeamPairs'))return false;
 if(geometry.drawRange.start!==0||geometry.drawRange.count!==Infinity||Object.values(geometry.morphAttributes).some(attributes=>attributes?.length))return false;
 const position=geometry.getAttribute('position');
 if(!position||position.itemSize!==3||!position.count||(geometry.index?.count??position.count)%3!==0)return false;
 for(const attribute of Object.values(geometry.attributes)){
  if(!(attribute instanceof THREE.BufferAttribute)||(attribute as THREE.InstancedBufferAttribute).isInstancedBufferAttribute||attribute.count!==position.count)return false;
  if(attribute instanceof THREE.Float16BufferAttribute||attribute.usage!==THREE.StaticDrawUsage||attribute.onUploadCallback!==THREE.BufferAttribute.prototype.onUploadCallback)return false;
 }
 if(geometry.index&&(geometry.index.usage!==THREE.StaticDrawUsage||geometry.index.onUploadCallback!==THREE.BufferAttribute.prototype.onUploadCallback))return false;
 const rest=geometry.userData.restShape;
 if(rest!==undefined&&(!(rest instanceof Float32Array)||rest.length!==position.array.length))return false;
 const pairs=geometry.userData.normalSeamPairs;
 if(pairs!==undefined&&(!Array.isArray(pairs)||pairs.some(pair=>!Array.isArray(pair)||pair.length!==2||pair.some(index=>!Number.isInteger(index)||index<0||index>=position.count))))return false;
 return true;
}

/** Concatenate untouched vertices and indices. Never bake a different shape origin. */
function mergeGeometry(meshes:THREE.Mesh[]){
 const geometry=new THREE.BufferGeometry(),names=Object.keys(meshes[0].geometry.attributes),parts:PlayerBatchPart[]=[],indices:number[]=[],normalSeamPairs:Array<[number,number]>=[];
 let vertexStart=0;
 for(const mesh of meshes){
  const source=mesh.geometry,vertexCount=source.getAttribute('position').count,indexStart=indices.length;
  if(source.index)for(let i=0;i<source.index.count;i++)indices.push(vertexStart+source.index.getX(i));
  else for(let i=0;i<vertexCount;i++)indices.push(vertexStart+i);
  const previous=mesh.userData.playerBatchParts as PlayerBatchPart[]|undefined;
  if(previous)for(const part of previous)parts.push({...part,vertexStart:vertexStart+part.vertexStart,indexStart:indexStart+part.indexStart});
  else parts.push({sourceId:mesh.uuid,name:mesh.name,vertexStart,vertexCount,indexStart,indexCount:indices.length-indexStart});
  for(const [first,last] of source.userData.normalSeamPairs??[])normalSeamPairs.push([vertexStart+first,vertexStart+last]);
  vertexStart+=vertexCount;
 }
 for(const name of names){
  const source=meshes[0].geometry.getAttribute(name) as THREE.BufferAttribute;
  const Constructor=source.array.constructor as new(length:number)=>THREE.TypedArray;
  const array=new Constructor(vertexStart*source.itemSize);let offset=0;
  for(const mesh of meshes){const values=(mesh.geometry.getAttribute(name) as THREE.BufferAttribute).array;array.set(values,offset);offset+=values.length;}
  const attribute=new THREE.BufferAttribute(array,source.itemSize,source.normalized);attribute.name=source.name;attribute.setUsage(source.usage);attribute.gpuType=source.gpuType;
  geometry.setAttribute(name,attribute);
 }
 geometry.setIndex(indices);
 // Body-shape edits recompute normals later. Preserve only declared smooth UV
 // seams, keeping each source's vertex identities and intentional hard edges.
 if(normalSeamPairs.length)geometry.userData.normalSeamPairs=normalSeamPairs;
 // dressPlayer can run before or after batching. Preserve immutable originals
 // so power -> lean -> athletic never scales already-scaled geometry again.
 if(meshes.some(mesh=>mesh.geometry.userData.restShape)){
  const rest=new Float32Array(vertexStart*3);let offset=0;
  for(const mesh of meshes){const source=mesh.geometry.userData.restShape??mesh.geometry.getAttribute('position').array;rest.set(source,offset);offset+=source.length;}
  geometry.userData.restShape=rest;
 }
 geometry.computeBoundingBox();geometry.computeBoundingSphere();
 return {geometry,parts};
}

/**
 * Merge owned, opaque static player meshes with identical motion parents,
 * materials, local transforms, draw flags, attributes and LOD membership.
 * Named hair and caller-supplied lettering handles remain independent.
 * detailMeshes is updated in place; skeletons and materials are never disposed.
 */
export function batchStaticPlayerMeshes(root:THREE.Object3D,detailMeshes:THREE.Mesh[],exclude:Iterable<THREE.Object3D>=[]):PlayerBatchReport{
 const excluded=new Set(exclude),detail=new Set(detailMeshes),groups=new Map<string,THREE.Mesh[]>();
 root.traverse(object=>{
  if(!(object instanceof THREE.Mesh)||!eligible(object,excluded))return;
  if(object.matrixAutoUpdate)object.updateMatrix();
  const key=JSON.stringify([object.parent!.uuid,object.material.uuid,object.matrix.toArray(),object.matrixAutoUpdate,object.matrixWorldAutoUpdate,
   object.visible,object.castShadow,object.receiveShadow,object.frustumCulled,object.renderOrder,object.layers.mask,detail.has(object),attributesKey(object.geometry)]);
  const group=groups.get(key);if(group)group.push(object);else groups.set(key,[object]);
 });
 const pending=[...groups.values()].filter(meshes=>meshes.length>1).map(meshes=>({meshes,...mergeGeometry(meshes)}));
 const replaced=new Map<THREE.Mesh,THREE.Mesh>(),oldGeometries=new Set<THREE.BufferGeometry>();
 for(const {meshes,geometry,parts} of pending){
  const first=meshes[0],parent=first.parent!,index=parent.children.indexOf(first),combined=new THREE.Mesh(geometry,first.material);combined.copy(first,false);combined.geometry=geometry;
  combined.name='Player static batch';combined.userData.playerBatchParts=parts;
  parent.add(combined);
  // Keep the first slot stable among opaque siblings, including unmerged details.
  parent.children.splice(parent.children.indexOf(combined),1);parent.children.splice(index,0,combined);
  for(const mesh of meshes){replaced.set(mesh,combined);oldGeometries.add(mesh.geometry);mesh.removeFromParent();}
 }
 const nextDetails=[...new Set(detailMeshes.map(mesh=>replaced.get(mesh)??mesh))];detailMeshes.splice(0,detailMeshes.length,...nextDetails);
 // An owned geometry can be referenced by several meshes. Do not release it
 // while an unmerged mesh elsewhere in the containing scene still uses it.
 let sceneRoot=root;while(sceneRoot.parent)sceneRoot=sceneRoot.parent;
 const retained=new Set<THREE.BufferGeometry>();sceneRoot.traverse(object=>{if(object instanceof THREE.Mesh)retained.add(object.geometry);});
 let disposedGeometries=0;for(const geometry of oldGeometries)if(!retained.has(geometry)){geometry.dispose();disposedGeometries++;}
 return {groups:pending.length,removedMeshes:replaced.size-pending.length,disposedGeometries};
}
