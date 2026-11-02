package org.fieldtak.hub.deployment

import java.time.Instant
import java.util.UUID

class DeploymentEngine(private val store:DeploymentStore) {
  fun start(descriptor:String? = null, packageUrl:String? = null):DeploymentSession {
    val now=Instant.now().toString()
    val s=DeploymentSession(UUID.randomUUID().toString(),"","","",descriptor,packageUrl,null,null,DeploymentStage.NEW,now,now)
    store.saveActive(s); return s
  }

  fun attachPackage(s:DeploymentSession, packageId:String, name:String, version:String, localPackagePath:String, extractedRoot:String):DeploymentSession =
    s.copy(packageId=packageId,packageName=name,packageVersion=version,localPackagePath=localPackagePath,extractedRoot=extractedRoot,updatedUtc=Instant.now().toString()).also(store::saveActive)

  fun transition(s:DeploymentSession, stage:DeploymentStage, result:StepResult, detail:String=""):DeploymentSession {
    val now=Instant.now().toString()
    val steps=s.steps.filterNot{it.stage==stage}+DeploymentStep(stage,result,detail,now)
    val updated=s.copy(stage=stage,updatedUtc=now,steps=steps)
    store.saveActive(updated); return updated
  }

  fun complete(s:DeploymentSession, detail:String="Telefon gotowy"):DeploymentSession {
    val now=Instant.now().toString()
    val done=s.copy(stage=DeploymentStage.COMPLETE,updatedUtc=now,completedUtc=now,
      steps=s.steps.filterNot{it.stage==DeploymentStage.COMPLETE}+DeploymentStep(DeploymentStage.COMPLETE,StepResult.READY,detail,now))
    store.archive(done); return done
  }

  fun fail(s:DeploymentSession, detail:String):DeploymentSession = transition(s,DeploymentStage.FAILED,StepResult.FAILED,detail)
}
