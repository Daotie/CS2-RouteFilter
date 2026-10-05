import React from "react";
import { trigger } from "cs2/api";
import mod from "mod.json";
import styles from "../route-filter.module.scss";
// Keep the established editor available if an optional surface fails.
export class FeatureBoundary extends React.Component<{children:React.ReactNode;onDismiss:()=>void},{failed:boolean}> {
  state={failed:false};
  static getDerivedStateFromError(){return {failed:true};}
  componentDidCatch(error:Error){console.error("[RouteFilter.UI] secondary surface failed",error);trigger(mod.id,"closeAdvancedInteraction");}
  render(){return this.state.failed?<button type="button" className={styles.featureRecovery} aria-label="RouteFilter" onClick={()=>{this.props.onDismiss();this.setState({failed:false});}}>RouteFilter ↻</button>:this.props.children;}
}
