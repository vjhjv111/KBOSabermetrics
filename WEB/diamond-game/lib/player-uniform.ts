import * as THREE from 'three';

/** The tucked hem follows the pelvis while the chest follows the shoulders. */
export function bindUniform(root:THREE.Group,hips:THREE.Group,torso:THREE.Group,panels:THREE.Mesh[]) {
 const waist=new THREE.Bone(),chest=new THREE.Bone();
 waist.name='Jersey waist';chest.name='Jersey chest';hips.add(waist);torso.add(chest);
 root.updateMatrixWorld(true);
 const skeleton=new THREE.Skeleton([waist,chest]);
 return panels.map(panel=>{
  const geometry=panel.geometry,position=geometry.getAttribute('position'),indices:number[]=[],weights:number[]=[];
  for(let i=0;i<position.count;i++){
   // Panels may be translated (wordmarks) or rotated (back lettering), but all
   // of them use the same torso-height weights as the cloth underneath.
   const height=position.getY(i)+panel.position.y;
   const upper=THREE.MathUtils.smoothstep(height,.055,.47);
   indices.push(0,1,0,0);weights.push(1-upper,upper,0,0);
  }
  geometry.setAttribute('skinIndex',new THREE.Uint16BufferAttribute(indices,4));
  geometry.setAttribute('skinWeight',new THREE.Float32BufferAttribute(weights,4));
  const cloth=new THREE.SkinnedMesh(geometry,panel.material);
  cloth.name=panel.name||'Tailored jersey detail';cloth.userData.uniformPanel=true;
  cloth.position.copy(panel.position);cloth.quaternion.copy(panel.quaternion);cloth.scale.copy(panel.scale);
  cloth.visible=panel.visible;cloth.castShadow=panel.castShadow;cloth.receiveShadow=panel.receiveShadow;
  cloth.renderOrder=panel.renderOrder;cloth.frustumCulled=false;
  panel.parent!.add(cloth);panel.removeFromParent();root.updateMatrixWorld(true);cloth.bind(skeleton);
  return cloth;
 });
}
