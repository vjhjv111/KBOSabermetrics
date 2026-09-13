import * as THREE from "three";
import {koreaTimeOfDay, type StadiumTimeOfDay} from "./korea-daylight";

/** Sunlight or fixed stadium floodlights, shared by all gameplay cameras. */
export function stadiumLighting(renderer: THREE.WebGLRenderer, scene: THREE.Scene, compact: boolean, timeOfDay: StadiumTimeOfDay = koreaTimeOfDay()) {
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
  const nightReflection = generator.fromScene(surroundings, .06, .1, 30);
  // Precompute both environments once. A clock transition never rebuilds the field
  // or allocates reflections in the middle of a pitch.
  surroundings.background.set("#b3d4ee");
  for (const card of cards) card.visible = false;
  const sunCard = cards[0]; sunCard.visible = true;
  sunCard.position.set(-5,8,3);sunCard.scale.set(.55,.55,.55);sunCard.lookAt(0,1,0);
  (sunCard.material as THREE.MeshBasicMaterial).color.setRGB(4,3.7,3.2);
  const dayReflection = generator.fromScene(surroundings, .06, .1, 30);
  generator.dispose();
  for (const card of cards) { card.geometry.dispose(); (card.material as THREE.Material).dispose(); }

  const focus = (side: "batter" | "pitcher") => {
    const z = side === "pitcher" ? -18.44 : 0;
    key.target.position.set(0,.95,z);
    key.target.updateMatrixWorld(true);
  };
  focus("batter");
  let disposed=false;
  let current: StadiumTimeOfDay | undefined;
  const setTimeOfDay = (phase: StadiumTimeOfDay) => {
    if (disposed || phase === current) return;
    current = phase;
    const day = phase === 'day';
    sky.name = 'Stadium ambient sky'; key.name = 'Stadium primary light';
    sky.color.set(day ? '#c3e1ff' : '#a7c5ed');
    sky.groundColor.set(day ? '#6e714c' : '#4a4634');
    sky.intensity = day ? 1.5 : .48;
    key.color.set(day ? '#fff5de' : '#fff2dc');
    key.intensity = day ? 3.1 : 2.8;
    key.position.set(day ? -48 : -57, day ? 82 : 38, day ? 20 : 7);
    key.updateMatrixWorld(true);
    fill.intensity = day ? .3 : 1.15;
    rim.intensity = day ? 0 : 1.8;
    scene.environment = (day ? dayReflection : nightReflection).texture;
    scene.environmentIntensity = day ? .48 : .34;
    renderer.toneMappingExposure = day ? .98 : 1.08;
  };
  setTimeOfDay(timeOfDay);
  return {focus, setTimeOfDay, dispose: () => { if(disposed)return;disposed=true;nightReflection.dispose();dayReflection.dispose();key.shadow.dispose();for(const light of [sky,key,fill,rim])light.removeFromParent();for(const light of [key,fill,rim])light.target.removeFromParent();if(scene.environment===nightReflection.texture||scene.environment===dayReflection.texture)scene.environment=null; }};
}
