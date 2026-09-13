import {useEffect, useState} from 'react';
import {koreaTimeOfDay, watchKoreaTimeOfDay} from './korea-daylight';

export function useKoreaTimeOfDay() {
 const [phase, setPhase] = useState(koreaTimeOfDay);
 useEffect(() => watchKoreaTimeOfDay(setPhase), []);
 return phase;
}
