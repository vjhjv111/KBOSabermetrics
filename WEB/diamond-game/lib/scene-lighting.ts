import * as THREE from "three";

/** Fixed floodlight directions persist across batting, pitching and tracking cameras. */
export function stadiumLighting(renderer: THREE.WebGLRenderer, scene: THREE.Scene, compact: boolean) {
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.08;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFShadowMap;

  const sky = new THREE.HemisphereLight("#a7c5ed", "#4a4634", .48); scene.add(sky);
  const key = new THREE.DirectionalLight("#fff2dc", 2.8);
  key.position.set(-57,38,7);
  key.castShadow = true;
  key.shadow.mapSize.setScalar(compact ? 1024 : 2048);
  key.shadow.camera.left = key.shadow.camera.bottom = -4.2;
  key.shadow.camera.right = key.shadow.camera.top = 4.2;
  key.shadow.camera.near = 1;
  key.shadow.camera.far = 180;
  key.shadow.bias = -.00005;
  key.shadow.normalBias = .013;
  key.shadow.radius = 2;
  scene.add(key, key.target);
  const fill = new THREE.DirectionalLight("#dbe9ff", 1.15);
  fill.position.set(57,38,7);fill.target.position.set(0,0,-26);
  scene.add(fill, fill.target);
  const rim = new THREE.DirectionalLight("#d8e9ff", 1.8);
  rim.position.set(86,38,-84);rim.target.position.set(0,1,-22);
  scene.add(rim, rim.target);

  // Broad floodlight reflections give helmets and leather their own material response.
  const surroundings = new THREE.Scene();
  surroundings.background = new THREE.Color("#3b4c5b");
  const cards: THREE.Mesh[] = [];
  for (const [x,y,z,sx,sy,intensity] of [[-5,5,2,3,2,7],[5,5,2,3,2,5],[-6,4,-5,4,2,6],[6,4,-5,4,2,6]]) {
    const card = new THREE.Mesh(new THREE.PlaneGeometry(sx,sy), new THREE.MeshBasicMaterial({color: new THREE.Color().setScalar(intensity), side: THREE.DoubleSide}));
    card.position.set(x,y,z); card.lookAt(0,1,0); surroundings.add(card); cards.push(card);
  }
  const generator = new THREE.PMREMGenerator(renderer);
  const reflection = generator.fromScene(surroundings, .06, .1, 30);
  scene.environment = reflection.texture;
  scene.environmentIntensity = .34;
  generator.dispose();
  for (const card of cards) { card.geometry.dispose(); (card.material as THREE.Material).dispose(); }

  const focus = (side: "batter" | "pitcher") => {
    const z = side === "pitcher" ? -18.44 : 0;
    key.target.position.set(0,.95,z);
    key.target.updateMatrixWorld(true);
  };
  focus("batter");
  let disposed=false;
  return {focus, dispose: () => { if(disposed)return;disposed=true;reflection.dispose();key.shadow.dispose();for(const light of [sky,key,fill,rim])light.removeFromParent();for(const light of [key,fill,rim])light.target.removeFromParent();if(scene.environment===reflection.texture)scene.environment=null; }};
}
